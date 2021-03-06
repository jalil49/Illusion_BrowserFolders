﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using ActionGame;
using BepInEx.Harmony;
using HarmonyLib;
using Illusion.Extensions;
using KKAPI.Utilities;
using Manager;
using UnityEngine;
using UnityEngine.UI;

namespace BrowserFolders.Hooks.KK
{
    [BrowserType(BrowserType.Classroom)]
    public class ClassroomFolders : IFolderBrowser
    {
        private static string _currentRelativeFolder;
        private static FolderTreeView _folderTreeView;

        private static ClassRoomCharaFile _customCharaFile;
        private static Canvas _canvas;
        private static string _targetScene;

        public ClassroomFolders()
        {
            _folderTreeView = new FolderTreeView(Utils.NormalizePath(UserData.Path), Path.Combine(Utils.NormalizePath(UserData.Path), "chara/female/"));
            _folderTreeView.CurrentFolderChanged = OnFolderChanged;

            Harmony.CreateAndPatchAll(typeof(ClassroomFolders));
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ClassRoomCharaFile), "Start")]
        internal static void InitHook(ClassRoomCharaFile __instance)
        {
            _folderTreeView.CurrentFolder = _folderTreeView.DefaultPath;

            _customCharaFile = __instance;
            _canvas = __instance.transform.GetComponentInParent<Canvas>();

            _targetScene = Scene.Instance.AddSceneName;
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(ClassRoomCharaFile), "InitializeList")]
        internal static IEnumerable<CodeInstruction> InitializePatch(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                if (string.Equals(instruction.operand as string, "chara/female/", StringComparison.OrdinalIgnoreCase))
                {
                    //0x7E	ldsfld <field>	Push the value of the static field on the stack.
                    instruction.opcode = OpCodes.Ldsfld;
                    instruction.operand = typeof(ClassroomFolders).GetField(nameof(_currentRelativeFolder), BindingFlags.NonPublic | BindingFlags.Static);
                }

                yield return instruction;
            }
        }

        /// <summary>
        /// Make it possible to fill in class with random characters from all subfolders
        /// ChaControl.GetRandomFemaleCard(int) : ChaFileControl[]
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(ChaControl), nameof(ChaControl.GetRandomFemaleCard), typeof(int))]
        internal static bool RandomCharaPickOverride(int num, ref ChaFileControl[] __result)
        {
            if (KK_BrowserFolders.RandomCharaSubfolders?.Value != true) return true;

            var path = Path.Combine(UserData.Path, "chara/female");
            if (!Directory.Exists(path))
            {
                __result = new ChaFileControl[0];
                return false;
            }

            // Grab from all subdirs
            var results = Directory.GetFiles(path, "*.png", SearchOption.AllDirectories);
            // Try to load cards until enough load successfully
            __result = results.Shuffle().Attempt(f =>
            {
                var chaFileControl = new ChaFileControl();
                if (chaFileControl.LoadCharaFile(f, 1, true, true))
                {
                    if (chaFileControl.parameter.sex != 0)
                        return chaFileControl;
                }
                return null;
            }).Where(x => x != null).Take(num).ToArray();
            return false;
        }

        public void OnGui()
        {
            if (_canvas != null && _canvas.enabled && _targetScene == Scene.Instance.AddSceneName)
            {
                var screenRect = GetFullscreenBrowserRect();
                IMGUIUtils.DrawSolidBox(screenRect);
                GUILayout.Window(362, screenRect, TreeWindow, "Select character folder");
                IMGUIUtils.EatInputInRect(screenRect);
            }
            else
            {
                _folderTreeView?.StopMonitoringFiles();
            }
        }

        internal static Rect GetFullscreenBrowserRect()
        {
            return new Rect((int)(Screen.width * 0.015), (int)(Screen.height * 0.35f), (int)(Screen.width * 0.16), (int)(Screen.height * 0.4));
        }

        private static void OnFolderChanged()
        {
            _currentRelativeFolder = _folderTreeView.CurrentRelativeFolder;

            if (_customCharaFile == null) return;

            _customCharaFile.InitializeList();

            // Fix add info toggle breaking
            var tglField = Traverse.Create(_customCharaFile).Field("listCtrl").Field("tglAddInfo");
            if (tglField.FieldExists())
            {
                var tglInfo = tglField.GetValue<Toggle>();
                tglInfo.onValueChanged.Invoke(tglInfo.isOn);
            }
        }

        private static void TreeWindow(int id)
        {
            GUILayout.BeginVertical();
            {
                _folderTreeView.DrawDirectoryTree();

                GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(false));
                {
                    if (GUILayout.Button("Refresh thumbnails"))
                    {
                        _folderTreeView.ResetTreeCache();
                        OnFolderChanged();
                    }

                    GUILayout.Space(1);

                    GUILayout.Label("Open in explorer...");
                    if (GUILayout.Button("Current folder"))
                        Utils.OpenDirInExplorer(_folderTreeView.CurrentFolder);
                    if (GUILayout.Button("Screenshot folder"))
                        Utils.OpenDirInExplorer(Path.Combine(Utils.NormalizePath(UserData.Path), "cap"));
                    if (GUILayout.Button("Main game folder"))
                        Utils.OpenDirInExplorer(Path.GetDirectoryName(Utils.NormalizePath(UserData.Path)));
                }
                GUILayout.EndVertical();
            }
            GUILayout.EndVertical();
        }
    }
}
