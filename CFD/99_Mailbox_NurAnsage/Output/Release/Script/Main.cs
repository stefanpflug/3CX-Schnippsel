using CallFlow.CFD;
using CallFlow;
using MimeKit;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks.Dataflow;
using System.Threading.Tasks;
using System.Threading;
using System;
using TCX.Configuration;

namespace VMNurAnsage
{
   public class Main : ScriptBase<Main>, ICallflow, ICallflowProcessor
   {
      private bool executionStarted;
      private bool executionFinished;
      private bool disconnectFlowPending;

      private BufferBlock<AbsEvent> eventBuffer;

      private int currentComponentIndex;
      private List<AbsComponent> mainFlowComponentList;
      private List<AbsComponent> disconnectFlowComponentList;
      private List<AbsComponent> errorFlowComponentList;
      private List<AbsComponent> currentFlowComponentList;

      private LogFormatter logFormatter;
      private TimerManager timerManager;
      private Dictionary<string, Variable> variableMap;
      private TempWavFileManager tempWavFileManager;
      private PromptQueue promptQueue;
      private OnlineServices onlineServices;
      private OfficeHoursManager officeHoursManager;

      private CfdAppScope scope;

      private void DisconnectCallAndExitCallflow()
      {
         if (currentFlowComponentList == disconnectFlowComponentList)
            logFormatter.Trace("Callflow finished...");
         else
         {
            logFormatter.Trace("Callflow finished, disconnecting call...");
            MyCall.Terminate();
         }
      }

      private async Task ExecuteErrorFlow()
      {
         if (currentFlowComponentList == errorFlowComponentList)
         {
            logFormatter.Trace("Error during error handler flow, exiting callflow...");
            DisconnectCallAndExitCallflow();
         }
         else if (currentFlowComponentList == disconnectFlowComponentList)
         {
            logFormatter.Trace("Error during disconnect handler flow, exiting callflow...");
            executionFinished = true;
         }
         else
         {
            currentFlowComponentList = errorFlowComponentList;
            currentComponentIndex = 0;
            if (errorFlowComponentList.Count > 0)
            {
               logFormatter.Trace("Start executing error handler flow...");
               await ProcessStart();
            }
            else
            {
               logFormatter.Trace("Error handler flow is empty...");
               DisconnectCallAndExitCallflow();
            }
         }
      }

      private async Task ExecuteDisconnectFlow()
      {
         currentFlowComponentList = disconnectFlowComponentList;
         currentComponentIndex = 0;
         disconnectFlowPending = false;
         if (disconnectFlowComponentList.Count > 0)
         {
            logFormatter.Trace("Start executing disconnect handler flow...");
            await ProcessStart();
         }
         else
         {
            logFormatter.Trace("Disconnect handler flow is empty...");
            executionFinished = true;
         }
      }

      private EventResults CheckEventResult(EventResults eventResult)
      {
         if (eventResult == EventResults.MoveToNextComponent && ++currentComponentIndex == currentFlowComponentList.Count)
         {
            DisconnectCallAndExitCallflow();
            return EventResults.Exit;
         }
         else if (eventResult == EventResults.Exit)
            DisconnectCallAndExitCallflow();

         return eventResult;
      }

      private void InitializeVariables(string callID)
      {
         // Call variables
         variableMap["session.ani"] = new Variable(MyCall.Caller.CallerID);
         variableMap["session.callid"] = new Variable(callID);
         variableMap["session.dnis"] = new Variable(MyCall.DN.Number);
         variableMap["session.did"] = new Variable(MyCall.Caller.CalledNumber);
         variableMap["session.audioFolder"] = new Variable(Path.Combine(RecordingManager.Instance.AudioFolder, promptQueue.ProjectAudioFolder));
         variableMap["session.transferingExtension"] = new Variable(MyCall.ReferredByDN?.Number ?? string.Empty);
         variableMap["session.forwardingExtension"] = new Variable(MyCall.OnBehalfOf?.Number ?? string.Empty);

         // Standard variables
         variableMap["RecordResult.NothingRecorded"] = new Variable(RecordComponent.RecordResults.NothingRecorded);
         variableMap["RecordResult.StopDigit"] = new Variable(RecordComponent.RecordResults.StopDigit);
         variableMap["RecordResult.Completed"] = new Variable(RecordComponent.RecordResults.Completed);
         variableMap["MenuResult.Timeout"] = new Variable(MenuComponent.MenuResults.Timeout);
         variableMap["MenuResult.InvalidOption"] = new Variable(MenuComponent.MenuResults.InvalidOption);
         variableMap["MenuResult.ValidOption"] = new Variable(MenuComponent.MenuResults.ValidOption);
         variableMap["UserInputResult.Timeout"] = new Variable(UserInputComponent.UserInputResults.Timeout);
         variableMap["UserInputResult.InvalidDigits"] = new Variable(UserInputComponent.UserInputResults.InvalidDigits);
         variableMap["UserInputResult.ValidDigits"] = new Variable(UserInputComponent.UserInputResults.ValidDigits);
         variableMap["VoiceInputResult.Timeout"] = new Variable(VoiceInputComponent.VoiceInputResults.Timeout);
         variableMap["VoiceInputResult.InvalidInput"] = new Variable(VoiceInputComponent.VoiceInputResults.InvalidInput);
         variableMap["VoiceInputResult.ValidInput"] = new Variable(VoiceInputComponent.VoiceInputResults.ValidInput);
         variableMap["VoiceInputResult.ValidDtmfInput"] = new Variable(VoiceInputComponent.VoiceInputResults.ValidDtmfInput);

         // User variables
         variableMap["callflow$.ExtensionNo"] = new Variable("");
            variableMap["callflow$.ExtensionIVRPath"] = new Variable("");
            variableMap["callflow$.wavfile"] = new Variable("");
            variableMap["callflow$.wavfullpath"] = new Variable("");
            variableMap["RecordResult.NothingRecorded"] = new Variable(RecordComponent.RecordResults.NothingRecorded);
            variableMap["RecordResult.StopDigit"] = new Variable(RecordComponent.RecordResults.StopDigit);
            variableMap["RecordResult.Completed"] = new Variable(RecordComponent.RecordResults.Completed);
            variableMap["MenuResult.Timeout"] = new Variable(MenuComponent.MenuResults.Timeout);
            variableMap["MenuResult.InvalidOption"] = new Variable(MenuComponent.MenuResults.InvalidOption);
            variableMap["MenuResult.ValidOption"] = new Variable(MenuComponent.MenuResults.ValidOption);
            variableMap["UserInputResult.Timeout"] = new Variable(UserInputComponent.UserInputResults.Timeout);
            variableMap["UserInputResult.InvalidDigits"] = new Variable(UserInputComponent.UserInputResults.InvalidDigits);
            variableMap["UserInputResult.ValidDigits"] = new Variable(UserInputComponent.UserInputResults.ValidDigits);
            variableMap["VoiceInputResult.Timeout"] = new Variable(VoiceInputComponent.VoiceInputResults.Timeout);
            variableMap["VoiceInputResult.InvalidInput"] = new Variable(VoiceInputComponent.VoiceInputResults.InvalidInput);
            variableMap["VoiceInputResult.ValidInput"] = new Variable(VoiceInputComponent.VoiceInputResults.ValidInput);
            variableMap["VoiceInputResult.ValidDtmfInput"] = new Variable(VoiceInputComponent.VoiceInputResults.ValidDtmfInput);
            
        }

      private void InitializeComponents(ICallflow callflow, ICall myCall, string logHeader)
      {
         scope = CfdModule.Instance.CreateScope(callflow, myCall, logHeader);

         {
            cGetDialedExtension cGetDialedExtension1 = new cGetDialedExtension(onlineServices, officeHoursManager, scope, "cGetDialedExtension1", callflow, myCall, logHeader);
            mainFlowComponentList.Add(cGetDialedExtension1);
            VariableAssignmentComponent AssignVariable1 = scope.CreateComponent<VariableAssignmentComponent>("AssignVariable1");
            AssignVariable1.VariableName = "callflow$.ExtensionNo";
            AssignVariable1.VariableValueHandler = () => { return cGetDialedExtension1.DialedExtensionNo; };
            mainFlowComponentList.Add(AssignVariable1);
            VariableAssignmentComponent AssignVariable2 = scope.CreateComponent<VariableAssignmentComponent>("AssignVariable2");
            AssignVariable2.VariableName = "callflow$.ExtensionIVRPath";
            AssignVariable2.VariableValueHandler = () => { return CFDFunctions.CONCATENATE(Convert.ToString("/var/lib/3cxpbx/Instance1/Data/Ivr/Voicemail/Data/"),Convert.ToString(variableMap["callflow$.ExtensionNo"].Value)); };
            mainFlowComponentList.Add(AssignVariable2);
            TcxGetExtensionStatusComponent GetExtensionStatus1 = scope.CreateComponent<TcxGetExtensionStatusComponent>("GetExtensionStatus1");
            GetExtensionStatus1.ExtensionHandler = () => { return Convert.ToString(variableMap["callflow$.ExtensionNo"].Value); };
            mainFlowComponentList.Add(GetExtensionStatus1);
            cGetVMWavfile cGetVMWavfile1 = new cGetVMWavfile(onlineServices, officeHoursManager, scope, "cGetVMWavfile1", callflow, myCall, logHeader);
            cGetVMWavfile1.IVRPathSetter = () => { return variableMap["callflow$.ExtensionIVRPath"].Value; };
            cGetVMWavfile1.ProfilenameSetter = () => { return GetExtensionStatus1.CurrentProfileName; };
            mainFlowComponentList.Add(cGetVMWavfile1);
            PromptPlaybackComponent PromptPlayback2 = scope.CreateComponent<PromptPlaybackComponent>("PromptPlayback2");
            PromptPlayback2.AllowDtmfInput = true;
            PromptPlayback2.Prompts.Add(new AudioFilePrompt(() => { return Convert.ToString(cGetVMWavfile1.result_Fullfilename); }));
            mainFlowComponentList.Add(PromptPlayback2);
            DisconnectCallComponent DisconnectCall1 = scope.CreateComponent<DisconnectCallComponent>("DisconnectCall1");
            mainFlowComponentList.Add(DisconnectCall1);
            }
            {
            }
            {
            }
            

         // Add a final DisconnectCall component to the main and error handler flows, in order to complete pending prompt playbacks...
         DisconnectCallComponent mainAutoAddedFinalDisconnectCall = scope.CreateComponent<DisconnectCallComponent>("mainAutoAddedFinalDisconnectCall");
         DisconnectCallComponent errorHandlerAutoAddedFinalDisconnectCall = scope.CreateComponent<DisconnectCallComponent>("errorHandlerAutoAddedFinalDisconnectCall");
         mainFlowComponentList.Add(mainAutoAddedFinalDisconnectCall);
         errorFlowComponentList.Add(errorHandlerAutoAddedFinalDisconnectCall);
      }

      public Main()
      {
         this.executionStarted = false;
         this.executionFinished = false;
         this.disconnectFlowPending = false;

         this.eventBuffer = new BufferBlock<AbsEvent>();

         this.currentComponentIndex = 0;
         this.mainFlowComponentList = new List<AbsComponent>();
         this.disconnectFlowComponentList = new List<AbsComponent>();
         this.errorFlowComponentList = new List<AbsComponent>();
         this.currentFlowComponentList = mainFlowComponentList;

         this.timerManager = new TimerManager();
         this.timerManager.OnTimeout += (state) => eventBuffer.Post(new TimeoutEvent(state));
         this.variableMap = new Dictionary<string, Variable>();

         AbsTextToSpeechEngine textToSpeechEngine = null;
         AbsSpeechToTextEngine speechToTextEngine = null;
         this.onlineServices = new OnlineServices(textToSpeechEngine, speechToTextEngine);
      }

      public override void Start()
      {
         string callID = MyCall?.Caller["chid"] ?? "Unknown";
         string logHeader = $"VMNurAnsage - CallID {callID}";
         this.logFormatter = new LogFormatter(MyCall, logHeader, "Callflow");
         this.promptQueue = new PromptQueue(this, MyCall, "VMNurAnsage", logHeader);
         this.tempWavFileManager = new TempWavFileManager(logFormatter);
         this.timerManager.CallStarted();
         this.officeHoursManager = new OfficeHoursManager(MyCall);

         logFormatter.Info($"ConnectionStatus:`{MyCall.Status}`");

         if (MyCall.Status == ConnectionStatus.Ringing)
            MyCall.AssureMedia().ContinueWith(_ => StartInternal(logHeader, callID));
         else
            StartInternal(logHeader, callID);
      }

      private void StartInternal(string logHeader, string callID)
      {
         logFormatter.Trace("SetBackgroundAudio to false");
         MyCall.SetBackgroundAudio(false, new string[] { });

         logFormatter.Trace("Initialize components");
         InitializeComponents(this, MyCall, logHeader);
         logFormatter.Trace("Initialize variables");
         InitializeVariables(callID);

         MyCall.OnTerminated += () => eventBuffer.Post(new CallTerminatedEvent());
         MyCall.OnDTMFInput += x => eventBuffer.Post(new DTMFReceivedEvent(x));

         logFormatter.Trace("Start executing main flow...");
         eventBuffer.Post(new StartEvent());
         Task.Run(() => EventProcessingLoop());

         
      }

      public void PostStartEvent()
      {
         eventBuffer.Post(new StartEvent());
      }

      public void PostDTMFReceivedEvent(char digit)
      {
         eventBuffer.Post(new DTMFReceivedEvent(digit));
      }

      public void PostPromptPlayedEvent()
      {
         eventBuffer.Post(new PromptPlayedEvent());
      }

      public void PostTransferFailedEvent()
      {
         eventBuffer.Post(new TransferFailedEvent());
      }

      public void PostMakeCallResultEvent(bool result)
      {
         eventBuffer.Post(new MakeCallResultEvent(result));
      }

      public void PostCallTerminatedEvent()
      {
         eventBuffer.Post(new CallTerminatedEvent());
      }

      public void PostTimeoutEvent(object state)
      {
         eventBuffer.Post(new TimeoutEvent(state));
      }

      private async Task EventProcessingLoop()
      {
         executionStarted = true;
         while (!executionFinished)
         {
            AbsEvent evt = await eventBuffer.ReceiveAsync();
            await evt?.ProcessEvent(this);
         }

         if (scope != null) scope.Dispose();
      }

      public async Task ProcessStart()
      {
         try
         {
            EventResults eventResult;
            do
            {
               AbsComponent currentComponent = currentFlowComponentList[currentComponentIndex];
               logFormatter.Trace("Start executing component '" + currentComponent.Name + "'");
               eventResult = await currentComponent.Start(timerManager, variableMap, tempWavFileManager, promptQueue);
            }
            while (CheckEventResult(eventResult) == EventResults.MoveToNextComponent);

            if (eventResult == EventResults.Exit) executionFinished = true;
         }
         catch (Exception exc)
         {
            logFormatter.Error("Error executing last component: " + exc.ToString());
            await ExecuteErrorFlow();
         }
      }

      public async Task ProcessDTMFReceived(char digit)
      {
         try
         {
            AbsComponent currentComponent = currentFlowComponentList[currentComponentIndex];
            logFormatter.Trace("OnDTMFReceived for component '" + currentComponent.Name + "' - Digit: '" + digit + "'");
            EventResults eventResult = CheckEventResult(await currentComponent.OnDTMFReceived(timerManager, variableMap, tempWavFileManager, promptQueue, digit));
            if (eventResult == EventResults.MoveToNextComponent)
            {
               if (disconnectFlowPending)
                  await ExecuteDisconnectFlow();
               else
                  await ProcessStart();
            }
            else if (eventResult == EventResults.Exit)
               executionFinished = true;
         }
         catch (Exception exc)
         {
            logFormatter.Error("Error executing last component: " + exc.ToString());
            await ExecuteErrorFlow();
         }
      }

      public async Task ProcessPromptPlayed()
      {
         try
         {
            promptQueue.NotifyPlayFinished();
            AbsComponent currentComponent = currentFlowComponentList[currentComponentIndex];
            logFormatter.Trace("OnPromptPlayed for component '" + currentComponent.Name + "'");
            EventResults eventResult = CheckEventResult(await currentComponent.OnPromptPlayed(timerManager, variableMap, tempWavFileManager, promptQueue));
            if (eventResult == EventResults.MoveToNextComponent)
            {
               if (disconnectFlowPending)
                  await ExecuteDisconnectFlow();
               else
                  await ProcessStart();
            }
            else if (eventResult == EventResults.Exit)
               executionFinished = true;
         }
         catch (Exception exc)
         {
            logFormatter.Error("Error executing last component: " + exc.ToString());
            await ExecuteErrorFlow();
         }
      }

      public async Task ProcessTransferFailed()
      {
         try
         {
            AbsComponent currentComponent = currentFlowComponentList[currentComponentIndex];
            logFormatter.Trace("OnTransferFailed for component '" + currentComponent.Name + "'");
            EventResults eventResult = CheckEventResult(await currentComponent.OnTransferFailed(timerManager, variableMap, tempWavFileManager, promptQueue));
            if (eventResult == EventResults.MoveToNextComponent)
            {
               if (disconnectFlowPending)
                  await ExecuteDisconnectFlow();
               else
                  await ProcessStart();
            }
            else if (eventResult == EventResults.Exit)
               executionFinished = true;
         }
         catch (Exception exc)
         {
            logFormatter.Error("Error executing last component: " + exc.ToString());
            await ExecuteErrorFlow();
         }
      }

      public async Task ProcessMakeCallResult(bool result)
      {
         try
         {
            AbsComponent currentComponent = currentFlowComponentList[currentComponentIndex];
            logFormatter.Trace("OnMakeCallResult for component '" + currentComponent.Name + "' - Result: '" + result + "'");
            EventResults eventResult = CheckEventResult(await currentComponent.OnMakeCallResult(timerManager, variableMap, tempWavFileManager, promptQueue, result));
            if (eventResult == EventResults.MoveToNextComponent)
            {
               if (disconnectFlowPending)
                  await ExecuteDisconnectFlow();
               else
                  await ProcessStart();
            }
            else if (eventResult == EventResults.Exit)
               executionFinished = true;
         }
         catch (Exception exc)
         {
            logFormatter.Error("Error executing last component: " + exc.ToString());
            await ExecuteErrorFlow();
         }
      }

      public async Task ProcessCallTerminated()
      {
         try
         {
            if (executionStarted)
            {
               // First notify the call termination to the current component
               AbsComponent currentComponent = currentFlowComponentList[currentComponentIndex];
               logFormatter.Trace("OnCallTerminated for component '" + currentComponent.Name + "'");

               // Don't wrap around CheckEventResult, because the call has been already disconnected, 
               // and the following action to execute depends on the returned value.
               EventResults eventResult = await currentComponent.OnCallTerminated(timerManager, variableMap, tempWavFileManager, promptQueue);
               if (eventResult == EventResults.MoveToNextComponent)
               {
                  // Next, if the current component has completed its job, execute the disconnect flow
                  await ExecuteDisconnectFlow();
               }
               else if (eventResult == EventResults.Wait)
               {
                  // If the user component needs more events, wait for it to finish, and signal here that we need to execute
                  // the disconnect handler flow of the callflow next...
                  disconnectFlowPending = true;
               }
               else if (eventResult == EventResults.Exit)
                  executionFinished = true;
            }
         }
         catch (Exception exc)
         {
            logFormatter.Error("Error executing last component: " + exc.ToString());
            await ExecuteErrorFlow();
         }
         finally
         {
            // Finally, delete temporary files
            tempWavFileManager.DeleteFilesAndFolders();
         }
      }

      public async Task ProcessTimeout(object state)
      {
         try
         {
            AbsComponent currentComponent = currentFlowComponentList[currentComponentIndex];
            logFormatter.Trace("OnTimeout for component '" + currentComponent.Name + "'");
            EventResults eventResult = CheckEventResult(await currentComponent.OnTimeout(timerManager, variableMap, tempWavFileManager, promptQueue, state));
            if (eventResult == EventResults.MoveToNextComponent)
            {
               if (disconnectFlowPending)
                  await ExecuteDisconnectFlow();
               else
                  await ProcessStart();
            }
            else if (eventResult == EventResults.Exit)
               executionFinished = true;
         }
         catch (Exception exc)
         {
            logFormatter.Error("Error executing last component: " + exc.ToString());
            await ExecuteErrorFlow();
         }
      }


              // ------------------------------------------------------------------------------------------------------------
        // User Defined component
        // ------------------------------------------------------------------------------------------------------------
        public class cGetDialedExtension : AbsUserComponent
        {
            private OnlineServices onlineServices;
            private OfficeHoursManager officeHoursManager;
            private CfdAppScope scope;

            private ObjectExpressionHandler _DialedExtensionNoHandler = null;
            

            protected override void InitializeVariables()
            {
                componentVariableMap["callflow$.DialedExtensionNo"] = new Variable("");
                
            }

            protected override void InitializeComponents()
            {
                Dictionary<string, Variable> variableMap = componentVariableMap;
                {
            ExecuteCSharpCode1864467505ECCComponent ExecuteCSharpCode1 = new ExecuteCSharpCode1864467505ECCComponent("ExecuteCSharpCode1", callflow, myCall, logHeader);
            mainFlowComponentList.Add(ExecuteCSharpCode1);
            VariableAssignmentComponent AssignVariable1 = scope.CreateComponent<VariableAssignmentComponent>("AssignVariable1");
            AssignVariable1.VariableName = "callflow$.DialedExtensionNo";
            AssignVariable1.VariableValueHandler = () => { return ExecuteCSharpCode1.ReturnValue; };
            mainFlowComponentList.Add(AssignVariable1);
            }
            {
            }
            {
            }
            
            }
            
            public cGetDialedExtension(OnlineServices onlineServices, OfficeHoursManager officeHoursManager,
                CfdAppScope scope, string name, ICallflow callflow, ICall myCall, string logHeader) : base(name, callflow, myCall, logHeader)
            {
                this.onlineServices = onlineServices;
                this.officeHoursManager = officeHoursManager;
                this.scope = scope;
            }
     
            protected override void GetVariableValues()
            {
                if (_DialedExtensionNoHandler != null) componentVariableMap["callflow$.DialedExtensionNo"].Set(_DialedExtensionNoHandler());
                
            }
            
            public ObjectExpressionHandler DialedExtensionNoSetter { set { _DialedExtensionNoHandler = value; } }
            public object DialedExtensionNo { get { return componentVariableMap["callflow$.DialedExtensionNo"].Value; } }
            

            private bool IsServerInHoliday(ICall myCall)
            {
                Tenant tenant = myCall.PS.GetTenant();
                return tenant != null && tenant.IsHoliday(new DateTimeOffset(DateTime.Now));
            }

            private bool IsServerOfficeHourActive(ICall myCall)
            {
		            Tenant tenant = myCall.PS.GetTenant();
		            if (tenant == null) return false;
		
		            string overrideOfficeTime = tenant.GetPropertyValue("OVERRIDEOFFICETIME");
		            if (!String.IsNullOrEmpty(overrideOfficeTime))
		            {
		                if (overrideOfficeTime == "1") // Forced to in office hours
		                    return true;
		                else if (overrideOfficeTime == "2") // Forced to out of office hours
		                    return false;
		            }
		
		            DateTime nowDt = DateTime.Now;
		            if (tenant.IsHoliday(new DateTimeOffset(nowDt))) return false;
		
		            Schedule officeHours = tenant.Hours;
		            Nullable<bool> result = officeHours.IsActiveTime(nowDt);
		            return result.GetValueOrDefault(false);
            }
        }
public class ExecuteCSharpCode1864467505ECCComponent : ExternalCodeExecutionComponent
            {
                public List<CallFlow.CFD.Parameter> Parameters { get; } = new List<CallFlow.CFD.Parameter>();
                public ExecuteCSharpCode1864467505ECCComponent(string name, ICallflow callflow, ICall myCall, string projectName) : base(name, callflow, myCall, projectName) {}
                protected override object ExecuteCode()
                {
                    return test();
                }
            
            private object test()
                {
            // https://www.3cx.com/community/threads/get-extension-by-called-number.132441/post-630907
string retval="0";
if(myCall.Caller.DN is ExternalLine externalLine && myCall.IsInbound)
{
    string[] range;
    foreach (var a in externalLine.RoutingRules)
    {
        bool match = (a.Conditions.Condition.Type == RuleConditionType.BasedOnDID &&
        (
            a.Data == myCall.Caller.CalledNumber
            || (a.Data.StartsWith('*') && myCall.Caller.CalledNumber.EndsWith(a.Data[1..]))
        ))
        ||
        (
            a.Conditions.Condition.Type == RuleConditionType.BasedOnCallerID &&
            a.Data.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any
            (x =>
                x == "*"
                || x == myCall.Caller.CallerID
                || x.StartsWith('*') && myCall.Caller.CallerID.EndsWith(x[1..])
                || x.EndsWith('*') && myCall.Caller.CallerID.StartsWith(x[..^1])
                || x.StartsWith('*') && x.EndsWith('*') && myCall.Caller.CallerID.Contains(x[1..^1])
                || ((range = x.Split("-", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Length == 2 ?
                (range[0].Length == myCall.Caller.CallerID.Length && range[1].Length == myCall.Caller.CallerID.Length) && (range[0].CompareTo(myCall.Caller.CallerID) <= 0 && range[1].CompareTo(myCall.Caller.CallerID) >= 0) : false)
            )
        )
        ||
        (a.Conditions.Condition.Type == RuleConditionType.ForwardAll);
   
        if (match)
        {
            retval=(a.ForwardDestinations.OfficeHoursDestination).Internal?.Number?? "0";
            break;
        }
    }
}
else
{
    retval=myCall.Caller.AttachedData["extnumber"]?? "0";
}
return retval;    }
            }
                    // ------------------------------------------------------------------------------------------------------------
        // User Defined component
        // ------------------------------------------------------------------------------------------------------------
        public class cGetVMWavfile : AbsUserComponent
        {
            private OnlineServices onlineServices;
            private OfficeHoursManager officeHoursManager;
            private CfdAppScope scope;

            private ObjectExpressionHandler _IVRPathHandler = null;
            private ObjectExpressionHandler _ProfilenameHandler = null;
            private ObjectExpressionHandler _result_FullfilenameHandler = null;
            private ObjectExpressionHandler _wavDefaultHandler = null;
            private ObjectExpressionHandler _wavAvailableHandler = null;
            private ObjectExpressionHandler _wavAwayHandler = null;
            private ObjectExpressionHandler _wavOoOHandler = null;
            private ObjectExpressionHandler _wavCustom1Handler = null;
            private ObjectExpressionHandler _wavCustom2Handler = null;
            private ObjectExpressionHandler _FilenameHandler = null;
            

            protected override void InitializeVariables()
            {
                componentVariableMap["callflow$.IVRPath"] = new Variable("");
                componentVariableMap["callflow$.Profilename"] = new Variable("");
                componentVariableMap["callflow$.result_Fullfilename"] = new Variable("");
                componentVariableMap["callflow$.wavDefault"] = new Variable("");
                componentVariableMap["callflow$.wavAvailable"] = new Variable("");
                componentVariableMap["callflow$.wavAway"] = new Variable("");
                componentVariableMap["callflow$.wavOoO"] = new Variable("");
                componentVariableMap["callflow$.wavCustom1"] = new Variable("");
                componentVariableMap["callflow$.wavCustom2"] = new Variable("");
                componentVariableMap["callflow$.Filename"] = new Variable("");
                
            }

            protected override void InitializeComponents()
            {
                Dictionary<string, Variable> variableMap = componentVariableMap;
                {
            FileManagementComponent ReadWriteFile1 = scope.CreateComponent<FileManagementComponent>("ReadWriteFile1");
            ReadWriteFile1.Action = FileManagementComponent.Actions.Read;
            ReadWriteFile1.FileMode = System.IO.FileMode.Open;
            ReadWriteFile1.FileNameHandler = () => { return Convert.ToString(CFDFunctions.CONCATENATE(Convert.ToString(variableMap["callflow$.IVRPath"].Value),Convert.ToString("/greetings.xml"))); };
            ReadWriteFile1.FirstLineToReadHandler = () => { return Convert.ToInt32(0); };
            ReadWriteFile1.ReadToEndHandler = () => { return Convert.ToBoolean(true); };
            mainFlowComponentList.Add(ReadWriteFile1);
            TextAnalyzerComponent JsonXmlParser1 = scope.CreateComponent<TextAnalyzerComponent>("JsonXmlParser1");
            JsonXmlParser1.TextType = TextAnalyzerComponent.TextTypes.XML;
            JsonXmlParser1.TextHandler = () => { return Convert.ToString(ReadWriteFile1.Result); };
            JsonXmlParser1.Mappings.Add("string(/overrides/greeting[@profile='default']/@file)", "callflow$.wavDefault");
            JsonXmlParser1.Mappings.Add("string(/overrides/greeting[@profile='Available']/@file)", "callflow$.wavAvailable");
            JsonXmlParser1.Mappings.Add("string(/overrides/greeting[@profile='Away']/@file)", "callflow$.wavAway");
            JsonXmlParser1.Mappings.Add("string(/overrides/greeting[@profile='Out of office']/@file)", "callflow$.wavOoO");
            JsonXmlParser1.Mappings.Add("string(/overrides/greeting[@profile='Custom 1']/@file)", "callflow$.wavCustom1");
            JsonXmlParser1.Mappings.Add("string(/overrides/greeting[@profile='Custom 2']/@file)", "callflow$.wavCustom2");
            mainFlowComponentList.Add(JsonXmlParser1);
            ConditionalComponent CreateCondition1 = scope.CreateComponent<ConditionalComponent>("CreateCondition1");
            mainFlowComponentList.Add(CreateCondition1);
            CreateCondition1.ConditionList.Add(() => { return Convert.ToBoolean(CFDFunctions.EQUAL(variableMap["callflow$.Profilename"].Value,"Available")); });
            CreateCondition1.ContainerList.Add(scope.CreateComponent<SequenceContainerComponent>("cond1"));
            VariableAssignmentComponent AssignVariable1 = scope.CreateComponent<VariableAssignmentComponent>("AssignVariable1");
            AssignVariable1.VariableName = "callflow$.Filename";
            AssignVariable1.VariableValueHandler = () => { return variableMap["callflow$.wavAvailable"].Value; };
            CreateCondition1.ContainerList[0].ComponentList.Add(AssignVariable1);
            CreateCondition1.ConditionList.Add(() => { return Convert.ToBoolean(CFDFunctions.EQUAL(variableMap["callflow$.Profilename"].Value,"Away")); });
            CreateCondition1.ContainerList.Add(scope.CreateComponent<SequenceContainerComponent>("conditionalComponentBranch1"));
            VariableAssignmentComponent AssignVariable2 = scope.CreateComponent<VariableAssignmentComponent>("AssignVariable2");
            AssignVariable2.VariableName = "callflow$.Filename";
            AssignVariable2.VariableValueHandler = () => { return variableMap["callflow$.wavAway"].Value; };
            CreateCondition1.ContainerList[1].ComponentList.Add(AssignVariable2);
            CreateCondition1.ConditionList.Add(() => { return Convert.ToBoolean(CFDFunctions.EQUAL(variableMap["callflow$.Profilename"].Value,"Out of office")); });
            CreateCondition1.ContainerList.Add(scope.CreateComponent<SequenceContainerComponent>("conditionalComponentBranch2"));
            VariableAssignmentComponent AssignVariable3 = scope.CreateComponent<VariableAssignmentComponent>("AssignVariable3");
            AssignVariable3.VariableName = "callflow$.Filename";
            AssignVariable3.VariableValueHandler = () => { return variableMap["callflow$.wavOoO"].Value; };
            CreateCondition1.ContainerList[2].ComponentList.Add(AssignVariable3);
            CreateCondition1.ConditionList.Add(() => { return Convert.ToBoolean(CFDFunctions.EQUAL(variableMap["callflow$.Profilename"].Value,"Custom 1")); });
            CreateCondition1.ContainerList.Add(scope.CreateComponent<SequenceContainerComponent>("conditionalComponentBranch3"));
            VariableAssignmentComponent AssignVariable4 = scope.CreateComponent<VariableAssignmentComponent>("AssignVariable4");
            AssignVariable4.VariableName = "callflow$.Filename";
            AssignVariable4.VariableValueHandler = () => { return variableMap["callflow$.wavCustom1"].Value; };
            CreateCondition1.ContainerList[3].ComponentList.Add(AssignVariable4);
            CreateCondition1.ConditionList.Add(() => { return Convert.ToBoolean(CFDFunctions.EQUAL(variableMap["callflow$.Profilename"].Value,"Custom 2")); });
            CreateCondition1.ContainerList.Add(scope.CreateComponent<SequenceContainerComponent>("conditionalComponentBranch4"));
            VariableAssignmentComponent AssignVariable5 = scope.CreateComponent<VariableAssignmentComponent>("AssignVariable5");
            AssignVariable5.VariableName = "callflow$.Filename";
            AssignVariable5.VariableValueHandler = () => { return variableMap["callflow$.wavCustom2"].Value; };
            CreateCondition1.ContainerList[4].ComponentList.Add(AssignVariable5);
            ConditionalComponent CreateCondition2 = scope.CreateComponent<ConditionalComponent>("CreateCondition2");
            mainFlowComponentList.Add(CreateCondition2);
            CreateCondition2.ConditionList.Add(() => { return Convert.ToBoolean(CFDFunctions.EQUAL(variableMap["callflow$.Filename"].Value,"")); });
            CreateCondition2.ContainerList.Add(scope.CreateComponent<SequenceContainerComponent>("conditionalComponentBranch5"));
            VariableAssignmentComponent variableAssignmentComponent1 = scope.CreateComponent<VariableAssignmentComponent>("variableAssignmentComponent1");
            variableAssignmentComponent1.VariableName = "callflow$.Filename";
            variableAssignmentComponent1.VariableValueHandler = () => { return variableMap["callflow$.wavDefault"].Value; };
            CreateCondition2.ContainerList[0].ComponentList.Add(variableAssignmentComponent1);
            VariableAssignmentComponent AssignVariable6 = scope.CreateComponent<VariableAssignmentComponent>("AssignVariable6");
            AssignVariable6.VariableName = "callflow$.result_Fullfilename";
            AssignVariable6.VariableValueHandler = () => { return CFDFunctions.CONCATENATE(Convert.ToString(variableMap["callflow$.IVRPath"].Value),Convert.ToString("/"),Convert.ToString(variableMap["callflow$.Filename"].Value)); };
            mainFlowComponentList.Add(AssignVariable6);
            }
            {
            }
            {
            }
            
            }
            
            public cGetVMWavfile(OnlineServices onlineServices, OfficeHoursManager officeHoursManager,
                CfdAppScope scope, string name, ICallflow callflow, ICall myCall, string logHeader) : base(name, callflow, myCall, logHeader)
            {
                this.onlineServices = onlineServices;
                this.officeHoursManager = officeHoursManager;
                this.scope = scope;
            }
     
            protected override void GetVariableValues()
            {
                if (_IVRPathHandler != null) componentVariableMap["callflow$.IVRPath"].Set(_IVRPathHandler());
                if (_ProfilenameHandler != null) componentVariableMap["callflow$.Profilename"].Set(_ProfilenameHandler());
                if (_result_FullfilenameHandler != null) componentVariableMap["callflow$.result_Fullfilename"].Set(_result_FullfilenameHandler());
                if (_wavDefaultHandler != null) componentVariableMap["callflow$.wavDefault"].Set(_wavDefaultHandler());
                if (_wavAvailableHandler != null) componentVariableMap["callflow$.wavAvailable"].Set(_wavAvailableHandler());
                if (_wavAwayHandler != null) componentVariableMap["callflow$.wavAway"].Set(_wavAwayHandler());
                if (_wavOoOHandler != null) componentVariableMap["callflow$.wavOoO"].Set(_wavOoOHandler());
                if (_wavCustom1Handler != null) componentVariableMap["callflow$.wavCustom1"].Set(_wavCustom1Handler());
                if (_wavCustom2Handler != null) componentVariableMap["callflow$.wavCustom2"].Set(_wavCustom2Handler());
                if (_FilenameHandler != null) componentVariableMap["callflow$.Filename"].Set(_FilenameHandler());
                
            }
            
            public ObjectExpressionHandler IVRPathSetter { set { _IVRPathHandler = value; } }
            public object IVRPath { get { return componentVariableMap["callflow$.IVRPath"].Value; } }
            public ObjectExpressionHandler ProfilenameSetter { set { _ProfilenameHandler = value; } }
            public object Profilename { get { return componentVariableMap["callflow$.Profilename"].Value; } }
            public ObjectExpressionHandler result_FullfilenameSetter { set { _result_FullfilenameHandler = value; } }
            public object result_Fullfilename { get { return componentVariableMap["callflow$.result_Fullfilename"].Value; } }
            public ObjectExpressionHandler wavDefaultSetter { set { _wavDefaultHandler = value; } }
            public object wavDefault { get { return componentVariableMap["callflow$.wavDefault"].Value; } }
            public ObjectExpressionHandler wavAvailableSetter { set { _wavAvailableHandler = value; } }
            public object wavAvailable { get { return componentVariableMap["callflow$.wavAvailable"].Value; } }
            public ObjectExpressionHandler wavAwaySetter { set { _wavAwayHandler = value; } }
            public object wavAway { get { return componentVariableMap["callflow$.wavAway"].Value; } }
            public ObjectExpressionHandler wavOoOSetter { set { _wavOoOHandler = value; } }
            public object wavOoO { get { return componentVariableMap["callflow$.wavOoO"].Value; } }
            public ObjectExpressionHandler wavCustom1Setter { set { _wavCustom1Handler = value; } }
            public object wavCustom1 { get { return componentVariableMap["callflow$.wavCustom1"].Value; } }
            public ObjectExpressionHandler wavCustom2Setter { set { _wavCustom2Handler = value; } }
            public object wavCustom2 { get { return componentVariableMap["callflow$.wavCustom2"].Value; } }
            public ObjectExpressionHandler FilenameSetter { set { _FilenameHandler = value; } }
            public object Filename { get { return componentVariableMap["callflow$.Filename"].Value; } }
            

            private bool IsServerInHoliday(ICall myCall)
            {
                Tenant tenant = myCall.PS.GetTenant();
                return tenant != null && tenant.IsHoliday(new DateTimeOffset(DateTime.Now));
            }

            private bool IsServerOfficeHourActive(ICall myCall)
            {
		            Tenant tenant = myCall.PS.GetTenant();
		            if (tenant == null) return false;
		
		            string overrideOfficeTime = tenant.GetPropertyValue("OVERRIDEOFFICETIME");
		            if (!String.IsNullOrEmpty(overrideOfficeTime))
		            {
		                if (overrideOfficeTime == "1") // Forced to in office hours
		                    return true;
		                else if (overrideOfficeTime == "2") // Forced to out of office hours
		                    return false;
		            }
		
		            DateTime nowDt = DateTime.Now;
		            if (tenant.IsHoliday(new DateTimeOffset(nowDt))) return false;
		
		            Schedule officeHours = tenant.Hours;
		            Nullable<bool> result = officeHours.IsActiveTime(nowDt);
		            return result.GetValueOrDefault(false);
            }
        }

   }
}
