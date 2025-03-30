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

namespace _72_dynWeiterleitung_an
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
         variableMap["project$.ExtensionNr"] = new Variable("");
            variableMap["project$.AuswahlOpt"] = new Variable("");
            variableMap["project$.ZielNr"] = new Variable("");
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
            UserInputComponent InputDestination = scope.CreateComponent<UserInputComponent>("InputDestination");
            InputDestination.AllowDtmfInput = true;
            InputDestination.MaxRetryCount = 2;
            InputDestination.FirstDigitTimeout = 5000;
            InputDestination.InterDigitTimeout = 3000;
            InputDestination.FinalDigitTimeout = 2000;
            InputDestination.MinDigits = 1;
            InputDestination.MaxDigits = 14;
            InputDestination.ValidDigitList.AddRange(new char[] { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9' });
            InputDestination.StopDigitList.AddRange(new char[] { '#' });
            InputDestination.InitialPrompts.Add(new AudioFilePrompt(() => { return "Bitte geben Sie die Zielnummer der Weiterleitung ein oder.wav"; }));
            InputDestination.SubsequentPrompts.Add(new AudioFilePrompt(() => { return "leer50ms.wav"; }));
            InputDestination.InvalidDigitPrompts.Add(new AudioFilePrompt(() => { return "FehlerBeiDerEingabe-vicky.wav"; }));
            InputDestination.TimeoutPrompts.Add(new AudioFilePrompt(() => { return "DasHatZuLangeGedauertBitteNochEinmalProbieren-vicki.wav"; }));
            mainFlowComponentList.Add(InputDestination);
            ConditionalComponent InputDestination_Conditional = scope.CreateComponent<ConditionalComponent>("InputDestination_Conditional");
            mainFlowComponentList.Add(InputDestination_Conditional);
            InputDestination_Conditional.ConditionList.Add(() => { return InputDestination.Result == UserInputComponent.UserInputResults.ValidDigits; });
            InputDestination_Conditional.ContainerList.Add(scope.CreateComponent<SequenceContainerComponent>("InputDestination_Conditional_ValidInput"));
            InputDestination_Conditional.ConditionList.Add(() => { return InputDestination.Result == UserInputComponent.UserInputResults.InvalidDigits || InputDestination.Result == UserInputComponent.UserInputResults.Timeout; });
            InputDestination_Conditional.ContainerList.Add(scope.CreateComponent<SequenceContainerComponent>("InputDestination_Conditional_InvalidInput"));
            VariableAssignmentComponent variableAssignmentExtensionNr = scope.CreateComponent<VariableAssignmentComponent>("variableAssignmentExtensionNr");
            variableAssignmentExtensionNr.VariableName = "project$.ExtensionNr";
            variableAssignmentExtensionNr.VariableValueHandler = () => { return variableMap["session.ani"].Value; };
            mainFlowComponentList.Add(variableAssignmentExtensionNr);
            VariableAssignmentComponent variableAssignmentZielNr = scope.CreateComponent<VariableAssignmentComponent>("variableAssignmentZielNr");
            variableAssignmentZielNr.VariableName = "project$.ZielNr";
            variableAssignmentZielNr.VariableValueHandler = () => { return InputDestination.Buffer; };
            mainFlowComponentList.Add(variableAssignmentZielNr);
            setStateAndFWDestination setStateAndFWDestination1 = new setStateAndFWDestination(onlineServices, officeHoursManager, scope, "setStateAndFWDestination1", callflow, myCall, logHeader);
            setStateAndFWDestination1.strDestNoSetter = () => { return variableMap["project$.ZielNr"].Value; };
            setStateAndFWDestination1.strExtensionNoSetter = () => { return variableMap["project$.ExtensionNr"].Value; };
            mainFlowComponentList.Add(setStateAndFWDestination1);
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
         string logHeader = $"_72_dynWeiterleitung_an - CallID {callID}";
         this.logFormatter = new LogFormatter(MyCall, logHeader, "Callflow");
         this.promptQueue = new PromptQueue(this, MyCall, "_72_dynWeiterleitung_an", logHeader);
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
        public class setStateAndFWDestination : AbsUserComponent
        {
            private OnlineServices onlineServices;
            private OfficeHoursManager officeHoursManager;
            private CfdAppScope scope;

            private ObjectExpressionHandler _strDestNoHandler = null;
            private ObjectExpressionHandler _strExtensionNoHandler = null;
            

            protected override void InitializeVariables()
            {
                componentVariableMap["callflow$.strDestNo"] = new Variable("");
                componentVariableMap["callflow$.strExtensionNo"] = new Variable("");
                
            }

            protected override void InitializeComponents()
            {
                Dictionary<string, Variable> variableMap = componentVariableMap;
                {
            SetExtDNDDest1287229307ECCComponent SetExtDNDDest = new SetExtDNDDest1287229307ECCComponent("SetExtDNDDest", callflow, myCall, logHeader);
            SetExtDNDDest.Parameters.Add(new CallFlow.CFD.Parameter("strExtNr", () => { return variableMap["callflow$.strExtensionNo"].Value; }));
            SetExtDNDDest.Parameters.Add(new CallFlow.CFD.Parameter("strDestNr", () => { return variableMap["callflow$.strDestNo"].Value; }));
            mainFlowComponentList.Add(SetExtDNDDest);
            ConditionalComponent CreateCondition1 = scope.CreateComponent<ConditionalComponent>("CreateCondition1");
            mainFlowComponentList.Add(CreateCondition1);
            CreateCondition1.ConditionList.Add(() => { return Convert.ToBoolean(CFDFunctions.EQUAL(SetExtDNDDest.ReturnValue,0)); });
            CreateCondition1.ContainerList.Add(scope.CreateComponent<SequenceContainerComponent>("conditionalComponentBranch1"));
            TcxSetExtensionStatusComponent SetAvailable = scope.CreateComponent<TcxSetExtensionStatusComponent>("SetAvailable");
            SetAvailable.ExtensionHandler = () => { return Convert.ToString(variableMap["callflow$.strExtensionNo"].Value); };
            SetAvailable.ProfileNameHandler = () => { return "Available"; };
            CreateCondition1.ContainerList[0].ComponentList.Add(SetAvailable);
            PromptPlaybackComponent Weiterleitung_entfernt = scope.CreateComponent<PromptPlaybackComponent>("Weiterleitung_entfernt");
            Weiterleitung_entfernt.AllowDtmfInput = true;
            Weiterleitung_entfernt.Prompts.Add(new AudioFilePrompt(() => { return "Weiterleitung wurde entfernt.wav"; }));
            CreateCondition1.ContainerList[0].ComponentList.Add(Weiterleitung_entfernt);
            CreateCondition1.ConditionList.Add(() => { return Convert.ToBoolean(CFDFunctions.EQUAL(SetExtDNDDest.ReturnValue,4)); });
            CreateCondition1.ContainerList.Add(scope.CreateComponent<SequenceContainerComponent>("conditionalComponentBranch2"));
            TcxSetExtensionStatusComponent SetExtCustom2 = scope.CreateComponent<TcxSetExtensionStatusComponent>("SetExtCustom2");
            SetExtCustom2.ExtensionHandler = () => { return Convert.ToString(variableMap["callflow$.strExtensionNo"].Value); };
            SetExtCustom2.ProfileNameHandler = () => { return "Custom 2"; };
            CreateCondition1.ContainerList[1].ComponentList.Add(SetExtCustom2);
            PromptPlaybackComponent PlayDigits = scope.CreateComponent<PromptPlaybackComponent>("PlayDigits");
            PlayDigits.AllowDtmfInput = true;
            PlayDigits.Prompts.Add(new AudioFilePrompt(() => { return "IhreAnrufeWerdenAufDieFolgendeNummerWeitergeleitet-vicki.wav"; }));
            PlayDigits.Prompts.Add(new NumberPrompt(NumberPrompt.NumberFormats.OneByOne, () => { return Convert.ToString(variableMap["callflow$.strDestNo"].Value); }));
            CreateCondition1.ContainerList[1].ComponentList.Add(PlayDigits);
            }
            {
            }
            {
            }
            
            }
            
            public setStateAndFWDestination(OnlineServices onlineServices, OfficeHoursManager officeHoursManager,
                CfdAppScope scope, string name, ICallflow callflow, ICall myCall, string logHeader) : base(name, callflow, myCall, logHeader)
            {
                this.onlineServices = onlineServices;
                this.officeHoursManager = officeHoursManager;
                this.scope = scope;
            }
     
            protected override void GetVariableValues()
            {
                if (_strDestNoHandler != null) componentVariableMap["callflow$.strDestNo"].Set(_strDestNoHandler());
                if (_strExtensionNoHandler != null) componentVariableMap["callflow$.strExtensionNo"].Set(_strExtensionNoHandler());
                
            }
            
            public ObjectExpressionHandler strDestNoSetter { set { _strDestNoHandler = value; } }
            public object strDestNo { get { return componentVariableMap["callflow$.strDestNo"].Value; } }
            public ObjectExpressionHandler strExtensionNoSetter { set { _strExtensionNoHandler = value; } }
            public object strExtensionNo { get { return componentVariableMap["callflow$.strExtensionNo"].Value; } }
            

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
public class SetExtDNDDest1287229307ECCComponent : ExternalCodeExecutionComponent
            {
                public List<CallFlow.CFD.Parameter> Parameters { get; } = new List<CallFlow.CFD.Parameter>();
                public SetExtDNDDest1287229307ECCComponent(string name, ICallflow callflow, ICall myCall, string projectName) : base(name, callflow, myCall, projectName) {}
                protected override object ExecuteCode()
                {
                    return SetExtDNDDest(Convert.ToString(Parameters[0].Value), Convert.ToString(Parameters[1].Value));
                }
            
            private object SetExtDNDDest(string strExtNr, string strDestNr)
                {
            var ext = PhoneSystem.Root.GetDNByNumber(strExtNr) as Extension;
var profile=ext.FwdProfiles.Where( x => x.Name == "Custom 2").First(); // 'Available', 'Away', 'Out of office', 'Custom 1', 'Custom 2', maybe parameter?

var NewStatus=-1;
if( profile != null ) {
    // DestinationStruct need 3 parameter
    // 1 DestinationType: 'None', 'VoiceMail', 'Extension', 'Queue', 'RingGroup', 'IVR', 'External', 'Fax', 'Boomerang' (external number),
    //                    'Deflect', 'VoiceMailOfDestination', 'Callback' (reserved), 'RoutePoint' 
    // 2 internal DN, maybe select by parameter  - OR - 
    // 3 external number as string, maybe select by parameter

	// DestinationType anhand der Zielnummer (strDestNr) bestimmen
	bool isInternal = false;
	bool isExtension = false;
	bool isRG = false;
	bool isIVR = false;
	bool isQUEUE = false;
	
	// check if strDestNr is a internal extension
	var extension = PhoneSystem.Root.GetDNByNumber(strDestNr) as Extension;
	if( extension != null ) {
		isInternal = true;
		isExtension = true;
	}
	
	// check if strDestNr is a ringgroup
	var allRGs = PhoneSystem.Root.GetRingGroups();
	foreach (var rg in allRGs)
	{
		if (rg.Number == strDestNr) 
		{
			isInternal = true;
			isRG = true;
			break;
		}
	}
	
	// check if strDestNr is a IVR/receptionist
	var allIVRs = PhoneSystem.Root.GetIVRs();
	foreach (var ivr in allIVRs)
	{
		if (ivr.Number == strDestNr) 
		{
			isInternal = true;
			isIVR = true;
			break;
		}
	}
	
	// check if strDestNr is a Queue
	var allQUEUQs = PhoneSystem.Root.GetQueues();
	foreach (var queue in allQUEUQs)
	{
		if (queue.Number == strDestNr) 
		{
			isInternal = true;
			isQUEUE = true;
			break;
		}
	}



	// define the DestinationStruct depending on the type of the destination target....
	
	var dest = new DestinationStruct();
	if (strExtNr.Equals(strDestNr)) {
		// set state to available if destination number is the number of the extension
		NewStatus = 0;
	}
	else {
		if( isInternal) {
			if( isExtension ) {	
				dest.To=DestinationType.Extension; 
				dest.Internal=PhoneSystem.Root.GetDNByNumber(strDestNr);  // needed if internal destination 
			}
			else if( isRG ) {	

				dest.To=DestinationType.RingGroup; 
				dest.Internal=PhoneSystem.Root.GetDNByNumber(strDestNr);  // needed if internal destination 
			}
			else if( isIVR ) {	

				dest.To=DestinationType.IVR; 
				dest.Internal=PhoneSystem.Root.GetDNByNumber(strDestNr);  // needed if internal destination 
			}
			else if( isQUEUE ) {	

				dest.To=DestinationType.Queue; 
				dest.Internal=PhoneSystem.Root.GetDNByNumber(strDestNr);  // needed if internal destination 
			}
		}
		else {

			dest.To=DestinationType.External; 
			dest.External=strDestNr;                                // needed if external destination
		
		}
		

		NewStatus = 4;
	}

    
    if( NewStatus >0)
	{
		// Depending on the type of status (present or absent), the forwarding must be entered in other target fields
		if( profile.TypeOfRouting == RoutingType.Available) {
			var route=profile.AvailableRoute;                   // maybe select by parameter?
			route.NoAnswer.AllCalls = dest;
			route.NoAnswer.Internal = dest;
			route.Busy.AllCalls = route.NotRegistered.AllCalls = dest;
			route.Busy.Internal = route.NotRegistered.Internal = dest;
		}
		if( profile.TypeOfRouting == RoutingType.Away) {
			var route=profile.AwayRoute;
			var external = profile.AwayRoute.External;
			route.Internal.AllHours = dest;                     // maybe select by parameter?
			route.Internal.OutOfOfficeHours = dest;
			route.External.AllHours = dest;
			route.External.OutOfOfficeHours = dest;
		}
		ext.Save();
	}
	
}
return( NewStatus);    }
            }
            
   }
}
