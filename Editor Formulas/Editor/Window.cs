﻿using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;

namespace EditorFormulas 
{
	public class Window : EditorWindow, IHasCustomMenu {

		[SerializeField]
		private Texture2D downloadTexture;

		[SerializeField]
		private Texture2D updateTexture;

		[SerializeField]
		private Texture2D optionsTexture;

		WebHelper webHelper;

		Dictionary<MethodInfo, object[]> parameterValuesDictionary;
		Dictionary<MethodInfo, ParameterInfo[]> parametersDictionary;

		List<FormulaData> searchResults = new List<FormulaData>();

		private Vector2 scrollPos;

		public string searchText = string.Empty;

		Vector2 windowSize = new Vector2(300, 400);

		FormulaDataStore formulaDataStore;

		GUIContent downloadButtonGUIContent;
		GUIContent updateButtonGUIContent;
		GUIContent optionsButtonGUIContent;
		GUIContent[] waitSpinGUIContents;

		bool doRepaint = false;

		bool debugMode = false;
		bool DebugMode
		{
			get
			{
				return debugMode;
			}
			set
			{
				debugMode = value;
				if(webHelper != null)
				{
					webHelper.DebugMode = debugMode;
				}
			}
		}

		bool showHiddenFormulas = false;
		public bool ShowHiddenFormulas
		{
			get
			{
				return showHiddenFormulas;
			}
			private set
			{
				showHiddenFormulas = value;
				FilterBySearchText(searchText);
			}
		}

		bool showOnlineFormulas = true;
		public bool ShowOnlineFormulas
		{
			get
			{
				return showOnlineFormulas;
			}
			private set
			{
				showOnlineFormulas = value;
				FilterBySearchText(searchText);
			}
		}

		private GUIContent debugModeGUIContent = new GUIContent("Debug Mode");
		private GUIContent showHiddenFormulasGUIContent = new GUIContent("Show Hidden Formulas");
		private GUIContent showOnlineFormulasGUIContent = new GUIContent("Show Online Formulas");

		private static Window instance;

		[MenuItem ("Window/Editor Formulas %#e")]
		public static void DoWindow()
		{
			var window = EditorWindow.GetWindow<Window>("Editor Formulas");
			var pos = window.position;
			pos.width = window.windowSize.x;
			pos.height = window.windowSize.y;
			window.position = pos;
		}

		void OnEnable()
		{
			instance = this;

			//Init formula data store
			formulaDataStore = FormulaDataStore.LoadFromAssetDatabaseOrCreate();
			//Init Web Helper
			webHelper = ScriptableObject.CreateInstance<WebHelper>();
			webHelper.Init(formulaDataStore);
			webHelper.FormulaDataUpdated += FormulaDataUpdated;

			DebugMode = EditorPrefs.GetBool(Constants.debugModePrefKey, false);
			//These two properties cause search results to be refreshed
			//So formulaDataStore needs to be initialized before these are set
			ShowHiddenFormulas = EditorPrefs.GetBool(Constants.showHiddenFormulasPrefKey, false);
			ShowOnlineFormulas = EditorPrefs.GetBool(Constants.showOnlineFormulasPrefKey, true);

			webHelper.DebugMode = DebugMode;

			//This code can be used to get the path to this class' path and use relative paths if necessary
			//Debug.Log("Path: " + AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this)));

			//Init GUIContents
			downloadButtonGUIContent = new GUIContent(downloadTexture, "Download Formula");
			updateButtonGUIContent = new GUIContent(updateTexture, "Update Available");
			optionsButtonGUIContent = new GUIContent(optionsTexture, "Options");

			waitSpinGUIContents = new GUIContent[12];
			for(int i=0; i<12; i++)
			{
				waitSpinGUIContents[i] = new GUIContent(EditorGUIUtility.FindTexture("WaitSpin" + i.ToString("00")));
			}

			LoadLocalFormulas();

			//Set up parameters
			var usableFormulas = formulaDataStore.FormulaData.FindAll(x => x.IsUsable);
			parametersDictionary = new Dictionary<MethodInfo, ParameterInfo[]>(usableFormulas.Count);
			parameterValuesDictionary = new Dictionary<MethodInfo, object[]>(usableFormulas.Count);
			foreach(var formula in usableFormulas)
			{
				var methodInfo = formula.methodInfo;
				parametersDictionary.Add(methodInfo, methodInfo.GetParameters());
				parameterValuesDictionary.Add(methodInfo, new object[methodInfo.GetParameters().Length]);
			}

			FilterBySearchText(searchText);

			webHelper.GetOnlineFormulas();
//			webHelper.CheckForAllFormulaUpdates();

			EditorApplication.update += OnUpdate;
		}

		void OnDisable()
		{
			if(webHelper != null)
			{
				UnityEngine.Object.DestroyImmediate(webHelper);
				webHelper.FormulaDataUpdated -= FormulaDataUpdated;
			}
			EditorApplication.update -= OnUpdate;
			instance = null;
		}

		void LoadLocalFormulas()
		{
			var methodList = new List<MethodInfo>(Utils.GetAllFormulaMethodsWithAttribute());

			foreach(var method in methodList)
			{
				//Process only if valid method was found
				if(method != null)
				{
					var formulaName = method.DeclaringType.Name;
					var formula = formulaDataStore.FormulaData.Find(x => x.name == formulaName);
					//If formula doesn't exist in formulaDataStore
					if(formula == null)
					{
						formula = new FormulaData();
						formula.name = formulaName;
						formula.projectFilePath = Constants.formulasFolderUnityPath + formulaName + ".cs";
						formulaDataStore.FormulaData.Add(formula);
						EditorUtility.SetDirty(formulaDataStore);
					}
					//Always write these values, even if formula already exists
					formula.localFileExists = new FileInfo(Utils.GetFullPathFromAssetsPath(formula.projectFilePath)).Exists;
					formula.methodInfo = method;
					var formulaAttribute = Utils.GetFormulaAttributeForMethodInfo(method);
					if(formulaAttribute != null)
					{
						formula.niceName = formulaAttribute.name;
						formula.tooltip = formulaAttribute.tooltip;
						formula.author = formulaAttribute.author;
					}
				}
			}

			//If there are local formulas in data store that point to local files that don't exist
			//and also don't have downloadURLs, remove them
			for(int i=formulaDataStore.FormulaData.Count-1; i>=0; i--)
			{
				var formulaData = formulaDataStore.FormulaData[i];
				var fullPath = Utils.GetFullPathFromAssetsPath(formulaData.projectFilePath);
				var fi = new FileInfo(fullPath);
				if(!fi.Exists && string.IsNullOrEmpty(formulaData.downloadURL))
				{
					formulaDataStore.FormulaData.RemoveAt(i);
					EditorUtility.SetDirty(formulaDataStore);
				}
			}
		}

		[UnityEditor.Callbacks.DidReloadScripts]
		private static void OnScriptsReloaded()
		{
//			Debug.Log("On scripts reload window is " + (instance == null ? "null" : "not null"));
			if(instance != null)
			{
				instance.Repaint();
			}
		}

		public void AddItemsToMenu(GenericMenu menu)
		{
			menu.AddItem(debugModeGUIContent, DebugMode, ToggleDebugMode);
			menu.AddItem(showHiddenFormulasGUIContent, ShowHiddenFormulas, ToggleShowHiddenFormulas);
			menu.AddItem(showOnlineFormulasGUIContent, ShowOnlineFormulas, ToggleShowOnlineFormulas);
		}

		void ToggleDebugMode()
		{
			DebugMode = !DebugMode;
			EditorPrefs.SetBool(Constants.debugModePrefKey, DebugMode);
		}

		void ToggleShowHiddenFormulas()
		{
			ShowHiddenFormulas = !ShowHiddenFormulas;
			EditorPrefs.SetBool(Constants.showHiddenFormulasPrefKey, ShowHiddenFormulas);
		}

		void ToggleShowOnlineFormulas()
		{
			ShowOnlineFormulas = !ShowOnlineFormulas;
			EditorPrefs.SetBool(Constants.showOnlineFormulasPrefKey, ShowOnlineFormulas);
		}

		void OnUpdate()
		{
			if(webHelper.DownloadingFormula)
			{
				doRepaint = true;
			}

			if(doRepaint)
			{
				this.Repaint();
				doRepaint = false;
			}
		}

		void OnGUI()
		{
			EditorGUI.BeginChangeCheck();
			searchText = EditorGUILayout.TextField(searchText);
			if(EditorGUI.EndChangeCheck())
			{
				FilterBySearchText(searchText);
			}

			scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

			//Draw search results
			for(int i=0; i<searchResults.Count; i++)
			{
				var formula = searchResults[i];

				if(formula.IsUsable)
				{
					DrawUsableFormula(formula);
				}
				else if(!formula.localFileExists)
				{
					DrawOnlineFormula(formula);
				}
			}

			EditorGUILayout.EndScrollView();
		}

		void DrawUsableFormula(FormulaData formula)
		{
			var method = formula.methodInfo;

			if(method == null)
			{
				return;
			}

			var parameters = parametersDictionary[method];
			var parameterValuesArray = parameterValuesDictionary[method];

			var hasParameters = parameters.Length > 0;

			if(hasParameters)
			{
				GUILayout.BeginVertical(GUI.skin.box, GUILayout.MaxWidth(this.position.width));
			}

			GUILayout.BeginHorizontal();

			var formulaButtonWidth = this.position.width - 32;
			if(hasParameters)
			{
				formulaButtonWidth -= 4;
			}
			if(formula.updateAvailable)
			{
				formulaButtonWidth -= 32;
			}

			//Commented out for now, not sure if necessary - Button is only enabled if parameters have been initialized
//			GUI.enabled = parameters.Length == 0 || parameterValuesArray.All(x => x != null);
			if(GUILayout.Button(new GUIContent(formula.niceName, formula.tooltip), GUILayout.Width(formulaButtonWidth)))
			{
				method.Invoke(null, parameterValuesArray);
			}
//			GUI.enabled = true;
			DrawOptionsButton(formula);

			if(formula.updateAvailable)
			{
				DrawDownloadButton(updateButtonGUIContent, formula);
			}

			GUILayout.EndHorizontal();

			if(hasParameters)
			{
				//Draw parameter fields
				for (int p=0; p<parameters.Length; p++)
				{
					var parameter = parameters[p];
					var parameterType = parameter.ParameterType;
					var niceParameterName = ObjectNames.NicifyVariableName(parameter.Name);
					var valueObj = parameterValuesArray[p];
					GUILayout.BeginHorizontal();
					object newValue = null;

	//				if(parameterType.IsClass && parameterType.IsSerializable)
	//				{
	//					var fieldInfos = parameterType.GetFields(BindingFlags.Instance | BindingFlags.Public);
	//					//TODO: Draw a field for each public instance field of class
	//				}

					EditorGUI.BeginChangeCheck();
					if (parameterType == typeof(int))
					{
						newValue = EditorGUILayout.IntField(new GUIContent(niceParameterName, niceParameterName), valueObj != null ? ((int) valueObj) : 0 );
					}
					else if(parameterType == typeof(float))
					{
						newValue = EditorGUILayout.FloatField(new GUIContent(niceParameterName, niceParameterName), valueObj != null ? ((float) valueObj) : 0f );
					}
					else if(parameterType == typeof(string))
					{
						newValue = EditorGUILayout.TextField(new GUIContent(niceParameterName, niceParameterName), valueObj != null ? ((string) valueObj) : string.Empty );
					}
					else if(parameterType == typeof(bool))
					{
						newValue = EditorGUILayout.Toggle(new GUIContent(niceParameterName, niceParameterName), valueObj != null ? ((bool) valueObj) : false);
					}
					else if(parameterType == typeof(Rect))
					{
						newValue = EditorGUILayout.RectField(new GUIContent(niceParameterName, niceParameterName), valueObj != null ? ((Rect) valueObj) : new Rect() );
					}
					//TODO: Don't do this, instead use RectOffset as a class
					else if(parameterType == typeof(RectOffset))
					{
						//We use a Vector4Field for RectOffset type because there isn't an Editor GUI drawer for rect offset
						var rectOffset = (RectOffset) valueObj;
						var vec4 = EditorGUILayout.Vector4Field(niceParameterName, valueObj != null ? new Vector4(rectOffset.left, rectOffset.right, rectOffset.top, rectOffset.bottom) : Vector4.zero );
						newValue = new RectOffset((int)vec4.x, (int)vec4.y, (int)vec4.z, (int)vec4.w);
					}
					else if(parameterType == typeof(Vector2))
					{
						var fieldWidth = EditorGUIUtility.fieldWidth;
						EditorGUIUtility.fieldWidth = 1f;
						newValue = EditorGUILayout.Vector2Field(new GUIContent(niceParameterName, niceParameterName), valueObj != null ? ((Vector2) valueObj) : Vector2.zero );
						EditorGUIUtility.fieldWidth = fieldWidth;
					}
					else if(parameterType == typeof(Vector3))
					{
						newValue = EditorGUILayout.Vector3Field(new GUIContent(niceParameterName, niceParameterName), valueObj != null ? ((Vector3) valueObj) : Vector3.zero );
					}
					else if(parameterType == typeof(Vector4))
					{
						newValue = EditorGUILayout.Vector4Field(niceParameterName, valueObj != null ? ((Vector4) valueObj) : Vector4.zero );
					}
					else if(parameterType == typeof(Color))
					{
						newValue = EditorGUILayout.ColorField(new GUIContent(niceParameterName, niceParameterName), valueObj != null ? ((Color) valueObj) : Color.white);
					}
					else if(parameterType == typeof(UnityEngine.Object))
					{
						newValue = EditorGUILayout.ObjectField(new GUIContent(niceParameterName, niceParameterName), valueObj != null ? ((UnityEngine.Object) valueObj) : null, parameterType, true);
					}
					else if(parameterType.IsEnum)
					{
						newValue = EditorGUILayout.EnumPopup(new GUIContent(niceParameterName, niceParameterName), valueObj != null ? ((System.Enum)valueObj) : default(System.Enum));
					
					}
					else if(parameterType == typeof(LayerMask))
					{
						newValue = Utils.LayerMaskField(niceParameterName, valueObj != null ? ((LayerMask)valueObj) : default(LayerMask));
					}
					if(EditorGUI.EndChangeCheck())
					{
						parameterValuesArray[p] = newValue;
					}
					GUILayout.EndHorizontal();
				}
			}

			if(hasParameters)
			{
				GUILayout.EndVertical();
			}
		}

		void DrawOnlineFormula(FormulaData formula)
		{
			var niceName = ObjectNames.NicifyVariableName(formula.name);
			//Button is disabled until formula is downloaded
			var guiEnabled = GUI.enabled;
			GUI.enabled = false;
			GUILayout.BeginHorizontal();
			GUILayout.Button(new GUIContent(niceName, niceName), GUILayout.MaxWidth(this.position.width - 60));
			GUI.enabled = guiEnabled;
			DrawDownloadButton(downloadButtonGUIContent, formula);
			DrawOptionsButton(formula);
			GUILayout.EndHorizontal();
		}

		void DrawOptionsButton(FormulaData formula)
		{
			if(GUILayout.Button(optionsButtonGUIContent, GUILayout.MaxWidth(20), GUILayout.MaxHeight(18)))
			{
				var menu = new GenericMenu();
				if(formula.localFileExists)
				{
					menu.AddItem(new GUIContent("Open in External Script Editor"), false, OpenFormulaInExternalScriptEditor, formula);
				}
				menu.AddItem(new GUIContent("Go to GitHub page"), false, GoToFormulaDownloadURL, formula);
				menu.AddItem(new GUIContent("Hide"), formula.hidden, ToggleFormulaHidden, formula);
				if(formula.localFileExists)
				{
					menu.AddItem(new GUIContent("Delete"), false, DeleteFormula, formula);
				}
				menu.ShowAsContext();
			}
		}

		void DrawDownloadButton(GUIContent defaultContent, FormulaData formula)
		{
			var guiContent = defaultContent;
			var diffInMilliseconds = DateTime.UtcNow.Subtract(formula.DownloadTimeUTC).TotalMilliseconds;
			bool compilingOrDownloadingFormula = (EditorApplication.isCompiling && diffInMilliseconds < 20000) || webHelper.IsDownloadingFormula(formula);
			//If the formula is in WebHelper's download queue or
			//the editor is compiling and download was less than 20 seconds ago, show spinner
			if(compilingOrDownloadingFormula)
			{
				int waitSpinIndex = Mathf.FloorToInt(((float)(diffInMilliseconds % 2000d) / 2000f) * 12f);
				guiContent = waitSpinGUIContents[waitSpinIndex];
				doRepaint = true;
			}

			if(GUILayout.Button(guiContent, GUILayout.MaxWidth(24), GUILayout.MaxHeight(18)))
			{
				//Button should do nothing if compiling or downloading formula
				if(!compilingOrDownloadingFormula)
				{
					webHelper.DownloadFormula(formula, false);
				}
			}
		}

		void FilterBySearchText(string text)
		{
			searchResults.Clear();
			searchResults.AddRange(formulaDataStore.FormulaData);
			searchResults.Sort((x,y) => x.name.CompareTo(y.name));

			if(! ShowHiddenFormulas)
			{
				searchResults.RemoveAll(x => x.hidden);
			}

			if(! ShowOnlineFormulas)
			{
				searchResults.RemoveAll(x => !(x.IsUsable) && !(x.localFileExists));
			}

			if(string.IsNullOrEmpty(text.Trim()))
			{
				return;
			}

			//If search text has multiple words, check each one and AND them
			var words = text.Split(new char[] {' '}, System.StringSplitOptions.RemoveEmptyEntries);
			if(words.Length == 0) { return; }

			//If there's only one word, check against normal method name, which has no spaces in it
			if(words.Length == 1)
			{
				//Remove all methods whose name doesn't contain search text
				searchResults.RemoveAll(x => 
					!x.name.ToLower ().Contains (text.Trim().ToLower ())
				);
			}
			//If there are multiple words, check that each one is contained in the nicified method name
			else
			{
				searchResults.RemoveAll(x => 
					{
						var niceMethodName = ObjectNames.NicifyVariableName(x.name).ToLower();
						bool allWordsContained = true;
						foreach(var word in words)
						{
							if(!niceMethodName.Contains(word.ToLower()))
							{
								allWordsContained = false;
								break;
							}
						}
						return !allWordsContained;
					}
				);
			}
		}

		void FormulaDataUpdated()
		{
			FilterBySearchText(searchText);
			this.Repaint();
		}

		void OpenFormulaInExternalScriptEditor (object obj)
		{
			var formulaData = obj as FormulaData;
			if(formulaData == null)
			{
				return;
			}
			AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<MonoScript>(formulaData.projectFilePath).GetInstanceID());
		}

		void GoToFormulaDownloadURL (object obj)
		{
			var formulaData = obj as FormulaData;
			if(formulaData == null)
			{
				return;
			}
			Application.OpenURL(formulaData.htmlURL);
		}

		void ToggleFormulaHidden (object obj)
		{
			var formulaData = obj as FormulaData;
			if(formulaData == null)
			{
				return;
			}

			formulaData.hidden = ! formulaData.hidden;
			EditorUtility.SetDirty(formulaDataStore);
			FilterBySearchText(searchText);
		}

		void DeleteFormula (object obj)
		{
			var formulaData = obj as FormulaData;
			if(formulaData == null)
			{
				return;
			}
			var fi = new FileInfo(Utils.GetFullPathFromAssetsPath(formulaData.projectFilePath));
			if(fi.Exists)
			{
				fi.Delete();
				AssetDatabase.Refresh();
			}
			formulaData.DownloadTimeUTC = DateTime.MinValue;
			formulaData.methodInfo = null;
			formulaData.localFileExists = false;
			EditorUtility.SetDirty(formulaDataStore);
			FilterBySearchText(searchText);
		}
	}
}