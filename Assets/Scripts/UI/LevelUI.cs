using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System;
using Eraflo.Common;
using Eraflo.Common.LevelSystem;
using System.IO;
namespace FallGuys.UI
{
    public class LevelUI : MonoBehaviour
    {   
        [SerializeField] private Button saveButton;
        [SerializeField] private SaveSystemManager saveSystem;
        [SerializeField] private LevelDatabase database;

        private string fullPath;
        private string relativePath;

        public string StringPath {
            get => Path.Combine(fullPath, relativePath);
            set => relativePath = value;
        }

        void Awake() {
            fullPath = Application.persistentDataPath;
            database.CreateNewLevel();

            saveButton.onClick.AddListener(Save);
        }

        public void Save()
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmssfff");
            string filename = $"Save_{timestamp}.json";
            string folder = "Saves";
            StringPath = folder;

            string finalPath = Path.Combine(folder, filename);

            if(!Directory.Exists(StringPath))
            {
                Directory.CreateDirectory(StringPath);
                
            }

            StringPath = finalPath;
            Debug.Log(StringPath);

            saveSystem.SaveToFile(StringPath, database.CurrentLevel);
            
        }
        
    }
}
