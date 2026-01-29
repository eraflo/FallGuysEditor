using System;
using System.Collections.Generic;
using System.Reflection;
using Eraflo.Common.ObjectSystem;
using Spatial;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FallGuys.UI
{
    /// <summary>
    /// World-space UI panel for editing LevelEditable parameters on objects.
    /// </summary>
    public class ObjectInspectorPanel : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private RectTransform contentContainer;
        [SerializeField] private Button confirmButton;
        [SerializeField] private Button cancelButton;

        [Header("Prefabs")]
        [SerializeField] private GameObject sliderFieldPrefab;
        [SerializeField] private GameObject inputFieldPrefab;
        [SerializeField] private GameObject toggleFieldPrefab;

        public event Action OnPanelClosed;

        private BaseObject _targetObject;
        private GhostController _ghostController;
        private List<FieldInfo> _editableFields = new List<FieldInfo>();
        private Dictionary<FieldInfo, object> _pendingValues = new Dictionary<FieldInfo, object>();
        private Dictionary<FieldInfo, object> _originalValues = new Dictionary<FieldInfo, object>();

        private void Awake()
        {
            if (confirmButton != null) confirmButton.onClick.AddListener(OnConfirm);
            if (cancelButton != null) cancelButton.onClick.AddListener(OnCancel);
            gameObject.SetActive(false);
        }

        public void Show(BaseObject target, Vector3 position, GhostController ghost)
        {
            if (target == null || target.RuntimeData?.Config == null)
            {
                Debug.LogWarning("[ObjectInspectorPanel] Invalid target or missing config.");
                return;
            }

            _targetObject = target;
            _ghostController = ghost;
            _editableFields = ParameterReflector.GetEditableFields(target.RuntimeData.Config);

            if (_editableFields.Count == 0)
            {
                Debug.Log("[ObjectInspectorPanel] No LevelEditable fields found on this object.");
                return;
            }

            // Position the panel
            transform.position = position;
            transform.LookAt(Camera.main.transform);
            transform.Rotate(0, 180, 0); // Face the camera

            // Setup Title
            if (titleText != null)
            {
                titleText.text = $"Edit: {target.RuntimeData.Config.name}";
            }

            // Clear previous UI
            foreach (Transform child in contentContainer)
            {
                Destroy(child.gameObject);
            }
            _pendingValues.Clear();
            _originalValues.Clear();

            // Generate UI for each field
            foreach (var field in _editableFields)
            {
                var attr = field.GetCustomAttribute<LevelEditableAttribute>();
                if (attr != null && !attr.ShowInInspector) continue;

                object currentValue = field.GetValue(target.RuntimeData.Config);
                _originalValues[field] = currentValue;
                _pendingValues[field] = currentValue;

                CreateFieldUI(field, currentValue);
            }

            gameObject.SetActive(true);
        }

        private void CreateFieldUI(FieldInfo field, object value)
        {
            Type type = field.FieldType;
            string label = ObjectNames.NicifyVariableName(field.Name);

            if (type == typeof(float) || type == typeof(int))
            {
                if (sliderFieldPrefab == null) return;

                GameObject go = Instantiate(sliderFieldPrefab, contentContainer);
                var labelText = go.GetComponentInChildren<TMP_Text>();
                var slider = go.GetComponentInChildren<Slider>();
                var valueText = go.transform.Find("ValueText")?.GetComponent<TMP_Text>();

                if (labelText != null) labelText.text = label;

                float floatValue = type == typeof(int) ? (int)value : (float)value;
                
                // Try to infer reasonable min/max
                float min = 0f;
                float max = floatValue > 0 ? floatValue * 3f : 10f;

                slider.minValue = min;
                slider.maxValue = max;
                slider.wholeNumbers = type == typeof(int);
                slider.value = floatValue;

                if (valueText != null) valueText.text = floatValue.ToString("F2");

                slider.onValueChanged.AddListener((v) =>
                {
                    _pendingValues[field] = type == typeof(int) ? (int)v : v;
                    if (valueText != null) valueText.text = v.ToString("F2");
                    UpdateGhostPreview();
                });
            }
            else if (type == typeof(bool))
            {
                if (toggleFieldPrefab == null) return;

                GameObject go = Instantiate(toggleFieldPrefab, contentContainer);
                var labelText = go.GetComponentInChildren<TMP_Text>();
                var toggle = go.GetComponentInChildren<Toggle>();

                if (labelText != null) labelText.text = label;
                toggle.isOn = (bool)value;

                toggle.onValueChanged.AddListener((v) =>
                {
                    _pendingValues[field] = v;
                    UpdateGhostPreview();
                });
            }
            else if (type == typeof(string))
            {
                if (inputFieldPrefab == null) return;

                GameObject go = Instantiate(inputFieldPrefab, contentContainer);
                var labelText = go.GetComponentInChildren<TMP_Text>();
                var inputField = go.GetComponentInChildren<TMP_InputField>();

                if (labelText != null) labelText.text = label;
                inputField.text = value?.ToString() ?? "";

                inputField.onValueChanged.AddListener((v) =>
                {
                    _pendingValues[field] = v;
                });
            }
            else if (type == typeof(Vector3))
            {
                CreateVector3UI(field, label, (Vector3)value);
            }
            else if (type == typeof(Quaternion))
            {
                // Quaternions are edited as Euler angles for usability
                Quaternion q = (Quaternion)value;
                CreateVector3UI(field, label + " (Euler)", q.eulerAngles, isQuaternion: true);
            }
            else if (type == typeof(Color))
            {
                CreateColorUI(field, label, (Color)value);
            }
            else if (type.IsEnum)
            {
                CreateEnumUI(field, label, value, type);
            }
        }

        private void CreateVector3UI(FieldInfo field, string label, Vector3 value, bool isQuaternion = false)
        {
            if (inputFieldPrefab == null) return;

            // Create a row with 3 input fields for X, Y, Z
            GameObject go = Instantiate(inputFieldPrefab, contentContainer);
            var labelText = go.GetComponentInChildren<TMP_Text>();
            if (labelText != null) labelText.text = label;

            // We'll reuse the single input field for now, but format it as "x, y, z"
            var inputField = go.GetComponentInChildren<TMP_InputField>();
            inputField.text = $"{value.x:F2}, {value.y:F2}, {value.z:F2}";

            inputField.onValueChanged.AddListener((v) =>
            {
                Vector3 parsed = ParseVector3(v);
                if (isQuaternion)
                {
                    _pendingValues[field] = Quaternion.Euler(parsed);
                }
                else
                {
                    _pendingValues[field] = parsed;
                }
                UpdateGhostPreview();
            });
        }

        private void CreateColorUI(FieldInfo field, string label, Color value)
        {
            if (sliderFieldPrefab == null) return;

            // Create 4 sliders for R, G, B, A
            string[] channels = { "R", "G", "B", "A" };
            float[] values = { value.r, value.g, value.b, value.a };
            Color pendingColor = value;

            for (int i = 0; i < 4; i++)
            {
                int channelIndex = i; // Capture for closure
                GameObject go = Instantiate(sliderFieldPrefab, contentContainer);
                var labelText = go.GetComponentInChildren<TMP_Text>();
                var slider = go.GetComponentInChildren<Slider>();
                var valueText = go.transform.Find("ValueText")?.GetComponent<TMP_Text>();

                if (labelText != null) labelText.text = $"{label} {channels[i]}";

                slider.minValue = 0f;
                slider.maxValue = 1f;
                slider.value = values[i];

                if (valueText != null) valueText.text = values[i].ToString("F2");

                slider.onValueChanged.AddListener((v) =>
                {
                    Color c = _pendingValues.ContainsKey(field) ? (Color)_pendingValues[field] : value;
                    switch (channelIndex)
                    {
                        case 0: c.r = v; break;
                        case 1: c.g = v; break;
                        case 2: c.b = v; break;
                        case 3: c.a = v; break;
                    }
                    _pendingValues[field] = c;
                    if (valueText != null) valueText.text = v.ToString("F2");
                    UpdateGhostPreview();
                });
            }
        }

        private void CreateEnumUI(FieldInfo field, string label, object value, Type enumType)
        {
            if (inputFieldPrefab == null) return;

            GameObject go = Instantiate(inputFieldPrefab, contentContainer);
            var labelText = go.GetComponentInChildren<TMP_Text>();
            var inputField = go.GetComponentInChildren<TMP_InputField>();

            // For now, use a simple text input. A dropdown would be ideal but requires a new prefab.
            if (labelText != null) labelText.text = label;
            inputField.text = value.ToString();

            // Show available options as placeholder
            string[] names = Enum.GetNames(enumType);
            inputField.placeholder.GetComponent<TMP_Text>().text = string.Join(", ", names);

            inputField.onValueChanged.AddListener((v) =>
            {
                try
                {
                    object parsed = Enum.Parse(enumType, v, true);
                    _pendingValues[field] = parsed;
                    UpdateGhostPreview();
                }
                catch { /* Invalid enum value, ignore */ }
            });
        }

        private Vector3 ParseVector3(string input)
        {
            try
            {
                string[] parts = input.Replace(" ", "").Split(',');
                if (parts.Length >= 3)
                {
                    return new Vector3(
                        float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture)
                    );
                }
            }
            catch { }
            return Vector3.zero;
        }


        private void UpdateGhostPreview()
        {
            if (_ghostController == null || _targetObject == null) return;

            // Show ghost at current position with pending modifications visualized
            // For now, just show the ghost; more complex previews would require
            // cloning the SO and applying pending values.
            _ghostController.Show(_targetObject.gameObject, _targetObject.transform.position, _targetObject.transform.rotation);
            _ghostController.SetValid(true);
        }

        private void OnConfirm()
        {
            // Apply pending values to the ObjectData overrides
            foreach (var kvp in _pendingValues)
            {
                ApplyOverride(kvp.Key, kvp.Value);
            }

            Close();
        }

        private void OnCancel()
        {
            Close();
        }

        private void Close()
        {
            gameObject.SetActive(false);
            _targetObject = null;
            OnPanelClosed?.Invoke();
        }

        private void ApplyOverride(FieldInfo field, object value)
        {
            if (_targetObject?.RuntimeData == null) return;

            // Find or create the override entry
            var overrides = _targetObject.RuntimeData.Overrides;
            var existing = overrides.Find(o => o.Name == field.Name);

            string serializedValue = value?.ToString() ?? "";
            string typeName = field.FieldType.AssemblyQualifiedName;

            if (existing != null)
            {
                existing.StringValue = serializedValue;
            }
            else
            {
                overrides.Add(new ParameterOverride
                {
                    Name = field.Name,
                    TypeName = typeName,
                    StringValue = serializedValue
                });
            }


            // Also apply directly to the config for immediate effect
            field.SetValue(_targetObject.RuntimeData.Config, value);
        }

        // Helper for nicer field names
        private static class ObjectNames
        {
            public static string NicifyVariableName(string name)
            {
                if (string.IsNullOrEmpty(name)) return name;

                // Remove leading underscore
                if (name.StartsWith("_")) name = name.Substring(1);

                // Insert spaces before capitals
                var result = new System.Text.StringBuilder();
                result.Append(char.ToUpper(name[0]));

                for (int i = 1; i < name.Length; i++)
                {
                    if (char.IsUpper(name[i]))
                    {
                        result.Append(' ');
                    }
                    result.Append(name[i]);
                }

                return result.ToString();
            }
        }
    }
}
