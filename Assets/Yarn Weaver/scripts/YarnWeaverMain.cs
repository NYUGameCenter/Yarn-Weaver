﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

using System;
using System.IO;
using System.Linq;

using Yarn.Unity;
using SFB;

namespace YarnWeaver {

	public class YarnWeaverMain : MonoBehaviour {

		public Button playButton;
		public Text filenameLabel;
		public Dropdown recentFilesList;
		public TextAsset sampleYarn;

		public GameObject sidebar, compileReadout; // sidebar is actually more like the "start menu" now
		public GameObject[] workArea;

		DialogueRunner dialogueRunner;
		YarnValidator validator;

		static string currentFilePath;
		List<string> previousFilePaths = new List<string>();

		static bool tutorialMode = false;
		bool noCompileErrors = false;

		const string prefsKey = "YarnWeaver_File";

		// Use this for initialization
		void Start () {
			// setup stuff
			validator = GetComponent<YarnValidator>();

			// build recent file list
			previousFilePaths.Clear(); // just to be safe
			while ( PlayerPrefs.HasKey( prefsKey + previousFilePaths.Count.ToString() )) {
				previousFilePaths.Add( PlayerPrefs.GetString( prefsKey + previousFilePaths.Count.ToString() ) );
			}

			if (previousFilePaths.Count > 0) {
				// convert that into dropdown population
				// we also want to remove the "file:///" if it's there
				recentFilesList.AddOptions( previousFilePaths.Select( x => (Path.GetFileNameWithoutExtension( x ) + "\n<size=10>" + ( x.StartsWith("file:///") ? x.Replace("file:///", "") : x) +"</size>").Replace("%20", " ") ).ToList() );
			}

			dialogueRunner = FindObjectOfType<DialogueRunner>();
			filenameLabel.text = "";

			// when user clicks "Refresh" button, it reloads the entire scene... this detects whether we should skip the start menu and go directly into the Yarn file
			if (tutorialMode == true) {
				OnClickLoadSample();
			} else if (currentFilePath != null && currentFilePath.Length > 0) {
				StartCoroutine( OutputRoutine( currentFilePath ) );
			}
		}
		
		// Update is called once per frame
		void Update () {
			bool isFileOpen = tutorialMode || (currentFilePath != null && currentFilePath.Length > 0);
			if (isFileOpen) {
				filenameLabel.text = tutorialMode ? "SAMPLE / TUTORIAL" : Path.GetFileNameWithoutExtension( currentFilePath.Replace( "%20", " " ) );
				filenameLabel.text += " > " + (dialogueRunner.isDialogueRunning && dialogueRunner.currentNodeName != null && dialogueRunner.currentNodeName.Length > 0 ? dialogueRunner.currentNodeName : "(STOPPED)" );
			}
			foreach (var go in workArea) {
				go.SetActive( isFileOpen );
			}
			sidebar.SetActive( !isFileOpen );
			compileReadout.SetActive( isFileOpen && !dialogueRunner.isDialogueRunning );
			playButton.gameObject.SetActive( tutorialMode || noCompileErrors );
		}

		public void OnClickRecentFile( int filepathIndex ) {
			// 0 is "open recent file" placeholder label, and doesn't count
			if (filepathIndex > 0) {
				StartCoroutine( OutputRoutine( previousFilePaths[filepathIndex - 1] ) );
			}
		}

		public void OnClickLoadSample() {
			tutorialMode = true;
			dialogueRunner.AddScript( sampleYarn );
			StartCurrentFile();
		}

		public void OnResetButtonClick () {
			SceneManager.LoadScene( SceneManager.GetActiveScene().buildIndex );
		}

		public void OnCloseButtonClick () {
			currentFilePath = "";
			tutorialMode = false;
			SceneManager.LoadScene( SceneManager.GetActiveScene().buildIndex );
		}

		// when the user clicks on "LOAD YARN FILE" from the start menu
		public void OnOpenButtonClick () {
			// start in desktop by default
			var startPath = System.Environment.GetFolderPath( System.Environment.SpecialFolder.Desktop );
			// but if the user has recent files, then start in the folder of the most recent file
			if ( previousFilePaths != null && previousFilePaths.Count > 0) {
				startPath = Path.GetDirectoryName( previousFilePaths[0].Replace("file:///", "") );
			}
			// let user use JSON or YARN.TXT
			var extensions = new [] {
				new ExtensionFilter("Yarn script files", "json", "txt" ),
				new ExtensionFilter("All Files", "*" ),
			};
			// ok actually do the FileOpen dialog now
			var paths = StandaloneFileBrowser.OpenFilePanel("select Yarn JSON or Yarn.TXT file...", startPath, extensions, false);
			if (paths.Length > 0) {
				StartCoroutine(OutputRoutine(new System.Uri(paths[0]).AbsoluteUri));
			}
		}

		// from the main menu
		public void OnClickQuitButton () {
			Application.Quit();
		}

		IEnumerator OutputRoutine(string url) {
			dialogueRunner.Stop();
			while (dialogueRunner.isDialogueRunning) {
				yield return 0;
			}
			dialogueRunner.Clear();
			var loader = new WWW(url);
			yield return loader;
			currentFilePath = url;
			previousFilePaths.Add( url );

			// try to add the script... if there are compile errors, we'll want to remember them for later
			var exceptions = new List<string>();
			try {
				dialogueRunner.AddScript( loader.text );
			} catch ( Exception ex ) {
				
				// to be extra helpful, let's show an excerpt from the broken script
				// 10 September 2017 -- OH NO, turns out we can't actually do this yet, due to how DialogueRunner works
				// 						I'll just leave this here, maybe investigate later
				/*
				if( ex.Message.StartsWith( "In file <input>: Error parsing node " ) ) {
					// extract node name
					var node = ex.Message.Substring( 36 ).Split( ':' )[0];
					// extract line number
					var lineNumberString = ex.Message.Split( new string[] {" Line ", ":"}, StringSplitOptions.None )[3];
					int lineNumber = -1;
					if( int.TryParse( lineNumberString, out lineNumber ) ) {
						// OH NO, turns out if it's broken, we can't actually get node data from DialogueRunner
						// I guess we can't actually do this, for now
					}
				}
				*/

				exceptions.Add( ex.Message );
			}
			// if we did have compile errors, we'll want to do other stuff (e.g. don't let user press Play button)
			noCompileErrors = exceptions.Count == 0;

			// save path into playerprefs
			if (previousFilePaths.Contains( currentFilePath )) {
				Debug.Log( "detecting duplicate filepaths in file history..." );
				previousFilePaths.RemoveAll( x => x == currentFilePath );
			}
			previousFilePaths.Insert( 0, currentFilePath );

			PlayerPrefs.DeleteAll();
			PlayerPrefs.Save();
			for (int i = 0; i < previousFilePaths.Count; i++) {
				PlayerPrefs.SetString( prefsKey + i.ToString(), previousFilePaths[i] );
			}
			PlayerPrefs.Save();

			yield return 0;

			// validate and analyze files
			// TODO: add support for multiple files
			var scriptData = new Dictionary<string, string>();
			scriptData.Add( Path.GetFileNameWithoutExtension(currentFilePath), loader.text );
			validator.StartMain( scriptData, exceptions );

			// load the file now (or try to!)
			if( noCompileErrors ) {
				StartCurrentFile();
			} 
		}

		string GetStartNode () {
			// search for a node that starts with "Start" (case-insensitive) or with the filename
			string filename = Path.GetFileNameWithoutExtension( currentFilePath );
			var startSearch = dialogueRunner.dialogue.allNodes.Where( x => x.ToLower().StartsWith("start") || x.ToLower().StartsWith(filename.ToLower()) ).ToArray();
			if (startSearch != null && startSearch.Length > 0) {
				return startSearch[0];
			} else { // otherwise, just go for the first node we find, which is usually the oldest
				return dialogueRunner.dialogue.allNodes.ToArray()[0];
			}
		}

		public void StartCurrentFile () {
			if (dialogueRunner.isDialogueRunning) {
				dialogueRunner.Stop();
			} else {
				dialogueRunner.startNode = GetStartNode();
				dialogueRunner.StartDialogue();
			}
		}
	}
}
