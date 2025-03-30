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

namespace _39_Profilstatus_mitExtension
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
            variableMap["project$.Zielstatus"] = new Variable("");
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
            UserInputComponent InputExtension = scope.CreateComponent<UserInputComponent>("InputExtension");
            InputExtension.AllowDtmfInput = true;
            InputExtension.MaxRetryCount = 2;
            InputExtension.FirstDigitTimeout = 5000;
            InputExtension.InterDigitTimeout = 3000;
            InputExtension.FinalDigitTimeout = 2000;
            InputExtension.MinDigits = 1;
            InputExtension.MaxDigits = 14;
            InputExtension.ValidDigitList.AddRange(new char[] { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9' });
            InputExtension.StopDigitList.AddRange(new char[] { '#' });
            InputExtension.InitialPrompts.Add(new AudioFilePrompt(() => { return "welcheNSt.wav"; }));
            InputExtension.SubsequentPrompts.Add(new AudioFilePrompt(() => { return "leer50ms.wav"; }));
            InputExtension.InvalidDigitPrompts.Add(new AudioFilePrompt(() => { return "FehlerBeiDerEingabe-vicky.wav"; }));
            InputExtension.TimeoutPrompts.Add(new AudioFilePrompt(() => { return "DasHatZuLangeGedauertBitteNochEinmalProbieren-vicki.wav"; }));
            mainFlowComponentList.Add(InputExtension);
            ConditionalComponent InputExtension_Conditional = scope.CreateComponent<ConditionalComponent>("InputExtension_Conditional");
            mainFlowComponentList.Add(InputExtension_Conditional);
            InputExtension_Conditional.ConditionList.Add(() => { return InputExtension.Result == UserInputComponent.UserInputResults.ValidDigits; });
            InputExtension_Conditional.ContainerList.Add(scope.CreateComponent<SequenceContainerComponent>("InputExtension_Conditional_ValidInput"));
            InputExtension_Conditional.ConditionList.Add(() => { return InputExtension.Result == UserInputComponent.UserInputResults.InvalidDigits || InputExtension.Result == UserInputComponent.UserInputResults.Timeout; });
            InputExtension_Conditional.ContainerList.Add(scope.CreateComponent<SequenceContainerComponent>("InputExtension_Conditional_InvalidInput"));
            VariableAssignmentComponent variableAssignmentExtensionNr = scope.CreateComponent<VariableAssignmentComponent>("variableAssignmentExtensionNr");
            variableAssignmentExtensionNr.VariableName = "project$.ExtensionNr";
            variableAssignmentExtensionNr.VariableValueHandler = () => { return InputExtension.Buffer; };
            mainFlowComponentList.Add(variableAssignmentExtensionNr);
            UserInputComponent InputStatus = scope.CreateComponent<UserInputComponent>("InputStatus");
            InputStatus.AllowDtmfInput = true;
            InputStatus.MaxRetryCount = 2;
            InputStatus.FirstDigitTimeout = 5000;
            InputStatus.InterDigitTimeout = 3000;
            InputStatus.FinalDigitTimeout = 2000;
            InputStatus.MinDigits = 1;
            InputStatus.MaxDigits = 1;
            InputStatus.ValidDigitList.AddRange(new char[] { '0', '1', '2', '3', '4' });
            InputStatus.StopDigitList.AddRange(new char[] { '#' });
            InputStatus.InitialPrompts.Add(new AudioFilePrompt(() => { return "welcher_Status.wav"; }));
            InputStatus.SubsequentPrompts.Add(new AudioFilePrompt(() => { return "leer50ms.wav"; }));
            InputStatus.InvalidDigitPrompts.Add(new AudioFilePrompt(() => { return "FehlerBeiDerEingabe-vicky.wav"; }));
            InputStatus.TimeoutPrompts.Add(new AudioFilePrompt(() => { return "DasHatZuLangeGedauertBitteNochEinmalProbieren-vicki.wav"; }));
            mainFlowComponentList.Add(InputStatus);
            ConditionalComponent InputStatus_Conditional = scope.CreateComponent<ConditionalComponent>("InputStatus_Conditional");
            mainFlowComponentList.Add(InputStatus_Conditional);
            InputStatus_Conditional.ConditionList.Add(() => { return InputStatus.Result == UserInputComponent.UserInputResults.ValidDigits; });
            InputStatus_Conditional.ContainerList.Add(scope.CreateComponent<SequenceContainerComponent>("InputStatus_Conditional_ValidInput"));
            InputStatus_Conditional.ConditionList.Add(() => { return InputStatus.Result == UserInputComponent.UserInputResults.InvalidDigits || InputStatus.Result == UserInputComponent.UserInputResults.Timeout; });
            InputStatus_Conditional.ContainerList.Add(scope.CreateComponent<SequenceContainerComponent>("InputStatus_Conditional_InvalidInput"));
            VariableAssignmentComponent variableAssignmentZielStatus = scope.CreateComponent<VariableAssignmentComponent>("variableAssignmentZielStatus");
            variableAssignmentZielStatus.VariableName = "project$.Zielstatus";
            variableAssignmentZielStatus.VariableValueHandler = () => { return InputStatus.Buffer; };
            mainFlowComponentList.Add(variableAssignmentZielStatus);
            ConditionalComponent CreateCondition1 = scope.CreateComponent<ConditionalComponent>("CreateCondition1");
            mainFlowComponentList.Add(CreateCondition1);
            CreateCondition1.ConditionList.Add(() => { return Convert.ToBoolean(CFDFunctions.EQUAL(variableMap["project$.Zielstatus"].Value,0)); });
            CreateCondition1.ContainerList.Add(scope.CreateComponent<SequenceContainerComponent>("conditionalComponentBranch1"));
            TcxSetExtensionStatusComponent SetExtensionStatus_Available = scope.CreateComponent<TcxSetExtensionStatusComponent>("SetExtensionStatus_Available");
            SetExtensionStatus_Available.ExtensionHandler = () => { return Convert.ToString(variableMap["project$.ExtensionNr"].Value); };
            SetExtensionStatus_Available.ProfileNameHandler = () => { return "Available"; };
            CreateCondition1.ContainerList[0].ComponentList.Add(SetExtensionStatus_Available);
            PromptPlaybackComponent PromptPlayback1 = scope.CreateComponent<PromptPlaybackComponent>("PromptPlayback1");
            PromptPlayback1.AllowDtmfInput = true;
            PromptPlayback1.Prompts.Add(new AudioFilePrompt(() => { return "Status0.wav"; }));
            CreateCondition1.ContainerList[0].ComponentList.Add(PromptPlayback1);
            CreateCondition1.ConditionList.Add(() => { return Convert.ToBoolean(CFDFunctions.EQUAL(variableMap["project$.Zielstatus"].Value,1)); });
            CreateCondition1.ContainerList.Add(scope.CreateComponent<SequenceContainerComponent>("conditionalComponentBranch2"));
            TcxSetExtensionStatusComponent SetExtensionStatus_Away = scope.CreateComponent<TcxSetExtensionStatusComponent>("SetExtensionStatus_Away");
            SetExtensionStatus_Away.ExtensionHandler = () => { return Convert.ToString(variableMap["project$.ExtensionNr"].Value); };
            SetExtensionStatus_Away.ProfileNameHandler = () => { return "Away"; };
            CreateCondition1.ContainerList[1].ComponentList.Add(SetExtensionStatus_Away);
            PromptPlaybackComponent promptPlaybackComponent1 = scope.CreateComponent<PromptPlaybackComponent>("promptPlaybackComponent1");
            promptPlaybackComponent1.AllowDtmfInput = true;
            promptPlaybackComponent1.Prompts.Add(new AudioFilePrompt(() => { return "Status1.wav"; }));
            CreateCondition1.ContainerList[1].ComponentList.Add(promptPlaybackComponent1);
            CreateCondition1.ConditionList.Add(() => { return Convert.ToBoolean(CFDFunctions.EQUAL(variableMap["project$.Zielstatus"].Value,2)); });
            CreateCondition1.ContainerList.Add(scope.CreateComponent<SequenceContainerComponent>("conditionalComponentBranch3"));
            TcxSetExtensionStatusComponent SetExtensionStatus_DND = scope.CreateComponent<TcxSetExtensionStatusComponent>("SetExtensionStatus_DND");
            SetExtensionStatus_DND.ExtensionHandler = () => { return Convert.ToString(variableMap["project$.ExtensionNr"].Value); };
            SetExtensionStatus_DND.ProfileNameHandler = () => { return "Out of office"; };
            CreateCondition1.ContainerList[2].ComponentList.Add(SetExtensionStatus_DND);
            PromptPlaybackComponent promptPlaybackComponent2 = scope.CreateComponent<PromptPlaybackComponent>("promptPlaybackComponent2");
            promptPlaybackComponent2.AllowDtmfInput = true;
            promptPlaybackComponent2.Prompts.Add(new AudioFilePrompt(() => { return "Sttatus2.wav"; }));
            CreateCondition1.ContainerList[2].ComponentList.Add(promptPlaybackComponent2);
            CreateCondition1.ConditionList.Add(() => { return Convert.ToBoolean(CFDFunctions.EQUAL(variableMap["project$.Zielstatus"].Value,3)); });
            CreateCondition1.ContainerList.Add(scope.CreateComponent<SequenceContainerComponent>("conditionalComponentBranch4"));
            TcxSetExtensionStatusComponent SetExtensionStatus_Custom1 = scope.CreateComponent<TcxSetExtensionStatusComponent>("SetExtensionStatus_Custom1");
            SetExtensionStatus_Custom1.ExtensionHandler = () => { return Convert.ToString(variableMap["project$.ExtensionNr"].Value); };
            SetExtensionStatus_Custom1.ProfileNameHandler = () => { return "Custom 1"; };
            CreateCondition1.ContainerList[3].ComponentList.Add(SetExtensionStatus_Custom1);
            PromptPlaybackComponent promptPlaybackComponent3 = scope.CreateComponent<PromptPlaybackComponent>("promptPlaybackComponent3");
            promptPlaybackComponent3.AllowDtmfInput = true;
            promptPlaybackComponent3.Prompts.Add(new AudioFilePrompt(() => { return "Status3.wav"; }));
            CreateCondition1.ContainerList[3].ComponentList.Add(promptPlaybackComponent3);
            CreateCondition1.ConditionList.Add(() => { return Convert.ToBoolean(CFDFunctions.EQUAL(variableMap["project$.Zielstatus"].Value,4)); });
            CreateCondition1.ContainerList.Add(scope.CreateComponent<SequenceContainerComponent>("conditionalComponentBranch5"));
            TcxSetExtensionStatusComponent SetExtensionStatus_Custom2 = scope.CreateComponent<TcxSetExtensionStatusComponent>("SetExtensionStatus_Custom2");
            SetExtensionStatus_Custom2.ExtensionHandler = () => { return Convert.ToString(variableMap["project$.ExtensionNr"].Value); };
            SetExtensionStatus_Custom2.ProfileNameHandler = () => { return "Custom 2"; };
            CreateCondition1.ContainerList[4].ComponentList.Add(SetExtensionStatus_Custom2);
            PromptPlaybackComponent promptPlaybackComponent4 = scope.CreateComponent<PromptPlaybackComponent>("promptPlaybackComponent4");
            promptPlaybackComponent4.AllowDtmfInput = true;
            promptPlaybackComponent4.Prompts.Add(new AudioFilePrompt(() => { return "Status4.wav"; }));
            CreateCondition1.ContainerList[4].ComponentList.Add(promptPlaybackComponent4);
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
         string logHeader = $"_39_Profilstatus_mitExtension - CallID {callID}";
         this.logFormatter = new LogFormatter(MyCall, logHeader, "Callflow");
         this.promptQueue = new PromptQueue(this, MyCall, "_39_Profilstatus_mitExtension", logHeader);
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


      
   }
}
