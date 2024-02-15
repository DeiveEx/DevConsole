using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using TMPro;
using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Ignix.DevConsole
{
	public enum TargetType
	{
		None,
		All,
		Single,
		Registry,
	}
	
	public class DevConsoleController : MonoBehaviour
	{
		#region SubTypes
		
		[Serializable]
		public class ConsoleCommand
		{
			public delegate void CommandFunction();

			public string name;
			public string description;
			public object instance;
			public MethodInfo methodInfo;
			public ParameterInfo[] parametersInfos;
			public string category;
			public CommandFunction dynamicMethod;
			public TargetType targetType;
			public Type sourceType;

			public string GetParametersString()
			{
				return string.Join(", ", parametersInfos.Select(x => x.ParameterType.Name));
			}
		}
		
		#endregion
		
		#region Fields

		public static DevConsoleController Instance;
		
		//Constants
		private const int maxCharacters = 15000;
		private WaitForEndOfFrame waitForEndOfFrame = new WaitForEndOfFrame();
		private Regex extractFuncRegex = new Regex(@"(?<func>\w+(?=\())\s*\((?<params>[^()]*)\)");
		private Regex extractArgsRegex = new Regex(@"(\[.+?\])|(\w+)");
		
		[Header("Debug")]
		public bool startOpened;
		public bool showFunctionInfo;
		[Header("Preferences")]
		[Tooltip("Allows commands that have no parameters to be called without the need to type the parenthesis.\nEx: \"help\" instead of \"help()\".\n\nNOTE: Only the fist command can take advantage of this, which means nested commands are required to have parenthesis.")]
		public bool allowQuickCommands = true;
		[Tooltip("If enabled, pressing ENTER will always autocomplete with the first suggestion, if available.")]
		public bool alwaysAutoComplete;
		public string consoleKey = "f1";
		public Color suggestionHighlight = Color.green;
		public int historySize = 100;
		[Header("UI Components")]
		public GameObject consoleScreen;
		public TMP_Text logText;
		public ScrollRect scrollRect;
		public TMP_InputField inputField;
		public GameObject suggestionBox;
		public TMP_Text suggestionText;
		[Header("Events")]
		public UnityEvent consoleOpen;
		public UnityEvent consoleClose;

		private RectTransform myTransform;
		private RectTransform scrollRectTransform;
		private RectTransform logTextTransform;
		private RectTransform suggestionTextTransform;
		private RectTransform suggestionBoxTransform;
		private List<ConsoleCommand> commands = new List<ConsoleCommand>();
		private List<string> suggestions = new List<string>();
		private List<string> commandHistory = new List<string>();
		private string currentText;
		private string currentCommand;
		private bool suggestionsSelected;
		private int selectedSuggestionID;
		private int selectedHistoryID;
		private Dictionary<string, object> heldParams = new Dictionary<string, object>();
		private Dictionary<Type, object> registry = new Dictionary<Type, object>();
		
		#endregion

		#region Unity Events
		
		private void Awake()
		{
			//Singleton
			if (Instance == null)
			{
				Instance = this;
			}
			else if(Instance != this)
			{
				Destroy(gameObject);
				return;
			}
			
			myTransform = GetComponent<RectTransform>();
			scrollRectTransform = scrollRect.GetComponent<RectTransform>();
			logTextTransform = logText.GetComponent<RectTransform>();
			suggestionTextTransform = suggestionText.GetComponent<RectTransform>();
			suggestionBoxTransform = suggestionBox.GetComponent<RectTransform>();
		}

		private void Start()
		{
			scrollRect.verticalNormalizedPosition = 0;
			logText.text = "";

			Application.logMessageReceived += AppendUnityLogLine;

			AppendLogLine("> Welcome to DevConsole! Type \"help\" to see a list of all registered commands.", Color.green);

			if (EventSystem.current == null)
			{
				Debug.LogError("No event system found! The console might not work!");
			}

			//Registering the basic commands
			RegisterBasicCommands();
			RegisterCommandsFromAttribute();

			suggestionBox.SetActive(false);

			ShowConsole(startOpened);
		}

		private void Update()
		{
			//Toggle console
			if (Input.GetKeyDown(consoleKey))
			{
				ShowConsole(!consoleScreen.activeSelf);
			}

			//When the console is active
			if (consoleScreen.activeSelf)
			{
				if (Input.GetKeyDown(KeyCode.Tab) && suggestionBox.activeInHierarchy)
				{
					ToggleSuggestions();
				}

				if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow))
				{
					//Select Suggestion
					if (suggestionBox.activeInHierarchy && suggestionsSelected)
					{
						SelectSuggestion();
					}
					else
					{
						SelectHistory();
					}

					MoveCaretToEnd();
				}
			}
		}
		
		#endregion

		#region Append Message
		
		public void AppendLog(string text, Color? color = null)
		{
			if (color != null && !string.IsNullOrEmpty(text))
			{
				text = $"<color=#{ColorUtility.ToHtmlStringRGBA((Color)color)}>{text}</color>";
			}

			if (logText.text.Length + text.Length > maxCharacters)
			{
				string[] entries = logText.text.Split('\n');
				int counter = 0;

				do
				{
					logText.text = logText.text.Substring(entries[counter].Length);
					counter++;
				} while (logText.text.Length + text.Length > maxCharacters);
			}

			logText.text += text;

			//Rebuild the layout
			LayoutRebuilder.MarkLayoutForRebuild(logTextTransform);
			LayoutRebuilder.MarkLayoutForRebuild(scrollRectTransform);

			GoToEndOfLog();
		}

		public void AppendLogLine(string line, Color? color = null)
		{
			AppendLog($"{line}\n", color);
		}

		public void AppendLogErrorLine(string line)
		{
			AppendLogLine($"ERROR: {line}", Color.red);
		}

		public void AppendUnityLogLine(string message, string stackTrace, LogType logType)
		{
			Color textColor = Color.cyan;
			AppendLog("UNITY", textColor);

			string logTypeText = logType != LogType.Log ? $" [{logType.ToString()}]" : string.Empty;

			if (logType == LogType.Assert || logType == LogType.Error || logType == LogType.Exception)
			{
				textColor = Color.red;
			}
			else if (logType == LogType.Warning)
			{
				textColor = Color.yellow;
			}

			AppendLog(logTypeText, textColor);
			AppendLog(": ", textColor);
			AppendLogLine(message);
		}
		
		#endregion

		#region Console Commands
		
		public void ShowConsole(bool show)
		{
			consoleScreen.SetActive(show);

			if (show)
			{
				ConsoleOpened();
			}
			else
			{
				ConsoleClosed();
			}
		}

		private void ConsoleOpened()
		{
			selectedSuggestionID = -1;
			selectedHistoryID = -1;
			suggestionsSelected = false;

			GoToEndOfLog();
			ReselectInput();

			consoleOpen.Invoke();
		}

		private void ConsoleClosed()
		{
			inputField.text = string.Empty;

			consoleClose.Invoke();
		}

		public void RegisterCommand(string name, string description, string methodName, object instance, string category = null)
		{
			//Get the method from the object instance using Reflection
			MethodInfo[] methods = instance.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
			methods = methods.Where(x => x.Name == methodName).ToArray();

			if (methods.Length == 0)
			{
				AppendLogErrorLine($"Could not find method \"{methodName}\" in the defined object instance for the command \"{name}\".");
				return;
			}

			for (int i = 0; i < methods.Length; i++)
			{
				ParameterInfo[] parametersInfo = methods[i].GetParameters();

				ConsoleCommand command = new ConsoleCommand()
				{
					name = name,
					description = description,
					instance = instance,
					methodInfo = methods[i],
					parametersInfos = parametersInfo,
					category = category,
					targetType = TargetType.None,
				};

				commands.Add(command);
			}
		}

		public void RegisterCommand(string name, string description, ConsoleCommand.CommandFunction function, string category = null)
		{
			ConsoleCommand command = new ConsoleCommand() {
				name = name,
				description = description,
				category = category,
				dynamicMethod = function,
				targetType = TargetType.None,
			};

			commands.Add(command);
		}

		private void AddCommandToHistory(string commandLine)
		{
			if (commandHistory.Count == 0 || (commandHistory.Count > 0 && commandHistory[0] != commandLine))
			{
				commandHistory.Insert(0, commandLine);

				//Removes the oldest entry when the size limit is reached
				if (commandHistory.Count >= historySize)
				{
					commandHistory.RemoveAt(commandHistory.Count - 1);
				}
			}
		}

		public void SubmitCommand(string commandLine)
		{
			//If the submit event was called by any other mean that wasn't the ENTER key, ignore.
			if (!Input.GetKey(KeyCode.Return))
			{
				return;
			}

			//If the input was empty, add an arrow to indicate a line was added
			if (string.IsNullOrEmpty(commandLine))
			{
				AppendLogLine(">");
				return;
			}

			//Either execute the command or autocomplete the text based on the suggestion
			if (suggestions.Count == 0 || (selectedSuggestionID == -1 && !alwaysAutoComplete) || (selectedSuggestionID == -1 && inputField.text == suggestions[0]) || (selectedSuggestionID >= 0 && inputField.text == suggestions[selectedSuggestionID]))
			{
				//Reset everything
				AppendLogLine("> " + commandLine);
				inputField.text = string.Empty;

				AddCommandToHistory(commandLine);
				selectedHistoryID = -1;

				suggestions.Clear();
				selectedSuggestionID = -1;
				suggestionsSelected = false;

				//Recursively call the functions written
				if (allowQuickCommands && !commandLine.Contains("("))
				{
					ExecuteCommand(commandLine, new object[0]);
				}
				else
				{
					string expression = commandLine;

					do
					{
						expression = ExtractFunction(expression);
					} while (extractFuncRegex.IsMatch(expression));

					heldParams.Clear();
				}
			}
			else if (suggestions.Count >= 1)
			{
				//Autocompletes the text based on the current selected suggestions
				string autoCompletedText = inputField.text;

				if (autoCompletedText.Contains("("))
				{
					autoCompletedText = autoCompletedText.Substring(0, autoCompletedText.LastIndexOf('(') + 1);
				}
				else
				{
					autoCompletedText = string.Empty;
				}

				if (alwaysAutoComplete && selectedSuggestionID == -1)
				{
					autoCompletedText += suggestions[0];
				}
				else
				{
					autoCompletedText += suggestions[selectedSuggestionID];
				}

				inputField.text = autoCompletedText;
				RebuildSuggestionBox();
			}
		}

		public void TextChanged(string text)
		{
			//Set the current text for the history
			if (!Input.GetKeyDown(KeyCode.UpArrow) && !Input.GetKeyDown(KeyCode.DownArrow))
			{
				currentText = text;
			}

			FindSuggestions(text);
		}

		public void RegisterObject<T>(T instance)
		{
			var type = typeof(T);

			if (registry.ContainsKey(type))
				Debug.LogWarning($"An object of type {type} is already registered in the registry. It will be replaced.");

			registry[type] = instance;
		}

		public void UnregisterObject<T>(T instance)
		{
			var type = typeof(T);
			registry.Remove(type);
		}
		
		#endregion

		#region Execution
		
		private string ExtractFunction(string commandText)
		{
			//Replace each match for a key that will be replaced with an object later on (if needed)
			if (!extractFuncRegex.IsMatch(commandText))
			{
				AppendLogErrorLine($"Command \"{commandText}\" was not implemented. Type \"help\" for a list of implemented commands.");
				return null;
			}

			return extractFuncRegex.Replace(commandText, match =>
			{
				string functionName = match.Groups["func"].Value;
				string parametersText = match.Groups["params"].Value;

				List<object> actualParameters = new List<object>();

				if (parametersText.Length > 0)
				{
					//Either convert the parameter string to a value or replace it with and object
					foreach (Match item in extractArgsRegex.Matches(parametersText))
					{
						string itemText = item.Value.Trim();

						if (heldParams.ContainsKey(itemText))
						{
							actualParameters.Add(heldParams[itemText]);
							heldParams.Remove(itemText);
						}
						else
						{
							actualParameters.Add(ConvertParameter(itemText));
						}
					}
				}

				if (showFunctionInfo)
				{
					Debug.Log($"\nFuncName: {functionName};\nParamCount: {actualParameters.Count};\nRawParams: {parametersText};\nFinalParams: {string.Join("; ", actualParameters.Select(x => x.ToString()))}");
				}

				//Add the key to the dictionary to be replaced later, if needed
				string key = functionName + match.Index;

				heldParams.Add(key, ExecuteCommand(functionName, actualParameters.ToArray()));

				return key;
			});
		}

		private object ExecuteCommand(string commandName, object[] parameters)
		{
			//See if there's at least one registered command that matches the name
			ConsoleCommand[] validCommands = commands.Where(x => x.name == commandName).ToArray();

			if (validCommands.Length == 0)
			{
				AppendLogErrorLine($"Command \"{commandName}\" was not implemented. Type \"help\" for a list of implemented commands.");
				return null;
			}

			//Find the command that has the same parameters types and Invoke the method
			foreach (var command in validCommands)
			{
				if(command.instance == null && command.dynamicMethod != null)
				{
					command.dynamicMethod.Invoke();
					return null;
				}

				if (parameters.Length == command.parametersInfos.Length)
				{
					try
					{
						if (command.targetType == TargetType.None)
						{
							object returnValue = command.methodInfo.Invoke(command.instance, parameters);
							return returnValue;
						}

						var targets = GetTargets(command.sourceType, command.targetType);
							
						if(targets.Length == 1)
							return command.methodInfo.Invoke(targets[0], parameters);

						foreach (var target in targets)
						{
							command.methodInfo.Invoke(target, parameters);
						}

						//We can't return multiple instances
						return null;
					}
					catch (Exception e)
					{
						Debug.LogError(e);
					}
				}
			}

			//If we couldn't execute any command, we show an error
			AppendLogErrorLine($"The parameters for the command \"{commandName}\" are invalid. Expected:");

			for (int i = 0; i < validCommands.Length; i++)
			{
				AppendLogLine($"{validCommands[i].name}({validCommands[i].GetParametersString()})", Color.red);
			}

			AppendLogLine($"Received:", Color.red);
			AppendLogLine($"({string.Join(";", parameters.Select(x => x.GetType()))})", Color.red);

			return null;
		}

		private object ConvertParameter(string parameter)
		{
			parameter = parameter.Trim();

			//Try for int
			if (int.TryParse(parameter, out int intValue))
			{
				return intValue;
			}

			//Try for float
			if (float.TryParse(parameter, out float floatValue))
			{
				return floatValue;
			}
			else if (parameter[parameter.Length - 1] == 'f')
			{
				if (float.TryParse(parameter.Substring(0, parameter.Length - 1), out floatValue))
				{
					return floatValue;
				}
			}

			//Try for Vector
			if (parameter.StartsWith("[") && parameter.EndsWith("]"))
			{
				string vectorBody = Regex.Replace(parameter, @"[\[\]]", string.Empty);
				string[] singleValues = Regex.Matches(vectorBody, @"(\w+\(.+?\))|(\[.+?\])|(\w+)")
					.Cast<Match>()
					.Select(x => x.Value)
					.ToArray(); //Get the array of values (as a MatchCollection) and converts it to an string array

				if (singleValues.Length < 2 || singleValues.Length > 4)
				{
					return null;
				}

				if (singleValues.Length == 2)
				{
					Vector2 vectorValue = new Vector2()
					{
						x = (float)Convert.ChangeType(ConvertParameter(singleValues[0]), typeof(float)),
						y = (float)Convert.ChangeType(ConvertParameter(singleValues[1]), typeof(float)),
					};

					return vectorValue;
				}
				else if (singleValues.Length == 3)
				{
					Vector3 vectorValue = new Vector3()
					{
						x = (float)Convert.ChangeType(ConvertParameter(singleValues[0]), typeof(float)),
						y = (float)Convert.ChangeType(ConvertParameter(singleValues[1]), typeof(float)),
						z = (float)Convert.ChangeType(ConvertParameter(singleValues[2]), typeof(float)),
					};

					return vectorValue;
				}
				else
				{
					Vector4 vectorValue = new Vector4()
					{
						x = (float)Convert.ChangeType(ConvertParameter(singleValues[0]), typeof(float)),
						y = (float)Convert.ChangeType(ConvertParameter(singleValues[1]), typeof(float)),
						z = (float)Convert.ChangeType(ConvertParameter(singleValues[2]), typeof(float)),
						w = (float)Convert.ChangeType(ConvertParameter(singleValues[3]), typeof(float)),
					};

					return vectorValue;
				}
			}

			//If all fails, we consider it a string
			return parameter;
		}

		private T ConvertParam<T>(object value)
		{
			return (T) Convert.ChangeType(value, typeof(T));
		}
		
		#endregion

		#region Suggestions
		
		private void FindSuggestions(string commandLine)
		{
			if (string.IsNullOrEmpty(commandLine))
			{
				suggestionBox.SetActive(false);
				return;
			}

			//Find any unique commands that has the typed text
			currentCommand = commandLine.Contains("(") ? commandLine.Substring(commandLine.LastIndexOf('(') + 1) : commandLine;
			suggestions = commands.Where(x => currentCommand.Length > 0 && x.name.StartsWith(currentCommand, StringComparison.InvariantCultureIgnoreCase)).Select(x => x.name).Distinct().ToList();

			RebuildSuggestionBox();
		}

		private void ToggleSuggestions()
		{
			suggestionsSelected = !suggestionsSelected;

			if (suggestionsSelected)
			{
				selectedSuggestionID = 0;
			}
			else
			{
				selectedSuggestionID = -1;
			}

			RebuildSuggestionBox();
		}

		private void SelectSuggestion()
		{
			//Change Suggestion
			if (Input.GetKeyDown(KeyCode.UpArrow))
			{
				selectedSuggestionID++;
			}
			else if (Input.GetKeyDown(KeyCode.DownArrow))
			{
				selectedSuggestionID--;
			}

			if (selectedSuggestionID >= suggestions.Count)
			{
				selectedSuggestionID = 0;
			}
			else if (selectedSuggestionID < 0)
			{
				selectedSuggestionID = suggestions.Count - 1;
			}

			//Update Layout
			RebuildSuggestionBox();
		}

		private void RebuildSuggestionBox()
		{
			if (suggestions.Count > 0)
			{
				suggestionBox.SetActive(true);

				//Fix the suggestion ID if needed
				if (selectedSuggestionID > suggestions.Count - 1)
				{
					selectedSuggestionID = suggestions.Count - 1;
				}

				//Clear the suggestion box
				suggestionText.text = string.Empty;

				//Add the suggestion text, highlighting the selected one
				for (int i = suggestions.Count - 1; i >= 0; i--)
				{
					string suggestionEntry = $"<color=#{ColorUtility.ToHtmlStringRGBA(suggestionHighlight)}>{suggestions[i].Substring(0, currentCommand.Length)}</color>{suggestions[i].Substring(currentCommand.Length)}\n";

					if (i == selectedSuggestionID)
					{
						suggestionText.text += $"<b><size=130%>{suggestionEntry}</size></b>";
					}
					else
					{
						suggestionText.text += $"{suggestionEntry}";
					}
				}
			}
			else
			{
				suggestionBox.SetActive(false);
				selectedSuggestionID = -1;
				suggestionsSelected = false;
			}

			LayoutRebuilder.ForceRebuildLayoutImmediate(suggestionTextTransform);
		}
		
		#endregion

		#region History
		
		private void SelectHistory()
		{
			//Change history
			if (Input.GetKeyDown(KeyCode.UpArrow))
			{
				selectedHistoryID++;
			}
			else if (Input.GetKeyDown(KeyCode.DownArrow))
			{
				selectedHistoryID--;
			}

			if (selectedHistoryID >= commandHistory.Count)
			{
				selectedHistoryID = commandHistory.Count - 1;
			}
			else if (selectedHistoryID <= -1)
			{
				selectedHistoryID = -1;
			}

			//Update Text/Layout
			if (selectedHistoryID == -1)
			{
				inputField.text = currentText;
			}
			else
			{
				inputField.text = commandHistory[selectedHistoryID];
			}
		}
		#endregion

		#region Helper Functions
		public void ReselectInput()
		{
			inputField.Select();
			inputField.ActivateInputField();
			MoveCaretToEnd();
		}

		private void MoveCaretToEnd()
		{
			inputField.caretPosition = inputField.text.Length;
		}

		public void GoToEndOfLog()
		{
			if (scrollRect.gameObject.activeInHierarchy)
			{
				StartCoroutine(GoToEndOfLog_Routine());
			}
		}

		private IEnumerator GoToEndOfLog_Routine()
		{
			yield return waitForEndOfFrame;
			scrollRect.verticalNormalizedPosition = 0;
		}

		private object[] GetTargets(Type type, TargetType targetType)
		{
			object[] targets = null;
			
			switch (targetType)
			{
				case TargetType.All:
					targets = FindObjectsOfType(type);
					break;
				case TargetType.Single:
					var single = FindObjectOfType(type);
					targets = new object[] {single};
					break;
				case TargetType.Registry:
					if (registry.TryGetValue(type, out var fromRegistry))
						targets = new object[] {fromRegistry};
					break;
			}

			return targets ?? Array.Empty<object>();
		}
		
		#endregion

		#region Basic Commands
		
		private void RegisterBasicCommands()
		{
			//You can register a command by using a Method name
			RegisterCommand("help", "Show all registered commands.", "ShowHelp", this, "Utility");
			RegisterCommand("clear", "Clear the console.", "ClearConsole", this, "Utility");
			RegisterCommand("print", "Print the typed text in the console a number of times.", "Print", this, "Utility");

			//Or by providing a parameterless function to the delegate
			RegisterCommand("test2", "Test.", () =>
			{
				Debug.Log(historySize);
			}, "Utility");
		}

		private void RegisterCommandsFromAttribute()
		{
			//Search for uses of the attribute
			var attributeType = typeof(DevCommandAttribute);
			
			foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
			{
				foreach (var type in assembly.GetTypes())
				{
					foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
					{
						var attributes = method.GetCustomAttributes(attributeType, false);
						
						if(attributes.Length == 0)
							continue;

						var attribute = (DevCommandAttribute) attributes[0];
						
						if(attribute.targetType != TargetType.None && !type.IsSubclassOf(typeof(MonoBehaviour)))
						{
							Debug.LogError($"Class of type {type} is not a {nameof(MonoBehaviour)}. TargetType {attribute.targetType} is can only be used with {nameof(MonoBehaviour)}");
							continue;
						}
						
						//Register the method as a command
						var parametersInfo = method.GetParameters();
						
						ConsoleCommand command = new ConsoleCommand()
						{
							name = attribute.name ?? method.Name,
							description = attribute.description,
							instance = null,
							methodInfo = method,
							parametersInfos = parametersInfo,
							category = attribute.category,
							targetType = attribute.targetType,
							sourceType = type,
						};
						
						commands.Add(command);
					}
				}
			}
		}

		private void ClearConsole()
		{
			logText.text = string.Empty;
		}

		private void ShowHelp()
		{
			Color helpColor = new Color(1, 0.5f, 0);

			AppendLogLine("\n=== Command List ===\n", helpColor);
			AppendLogLine("This is a list of all available commands:\n", helpColor);

			//Categorized commands
			List<string> categories = new List<string>();

			foreach (var item in commands)
			{
				if (item.category != null && !categories.Contains(item.category))
				{
					categories.Add(item.category);
					AppendLogLine($"# {item.category} #", helpColor);

					foreach (var categoryItem in commands.Where(x => x.category == item.category))
					{
						if (categoryItem.instance == null)
						{
							AppendLog($"- {categoryItem.name}(): ", helpColor);
							AppendLogLine($"{categoryItem.description}");
						}
						else
						{
							AppendLog($"- {categoryItem.name}({categoryItem.GetParametersString()}): ", helpColor);
							AppendLogLine($"{categoryItem.description}");
						}
					}

					AppendLogLine("");
				}
			}

			//Uncategorized
			if (commands.Any(x => x.category == null))
			{
				AppendLogLine($"# Uncategorized #", helpColor);

				foreach (var categoryItem in commands.Where(x => x.category == null))
				{
					AppendLog($"- {categoryItem.name}({categoryItem.GetParametersString()}): ", helpColor);
					AppendLogLine($"{categoryItem.description}");
				}
			}

			AppendLogLine("\nYou can use TAB to toggle between the history of commands and suggestions, and then use the UP and DOWN arrows to navigate.\n", helpColor);
			AppendLogLine("Vectors 2, 3 and 4 must be written between square brackets. Ex: a Vector2  must be written like \"[0, 0]\"", helpColor);

			AppendLogLine("\n> DevConsole developed by Felipe Carmo Cavaco (aka \"Deive_Ex\")", helpColor);
			AppendLogLine("Version 2.0", helpColor);
			AppendLogLine("\n=== End of the List ===\n", helpColor);
		}

		private void Print(string text, int amount)
		{
			for (int i = 0; i < amount; i++)
			{
				AppendLogLine(text);
			}
		}
		
		#endregion
	}
}
