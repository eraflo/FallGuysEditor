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
        // Keyboard Reference
        private Microsoft.MixedReality.Toolkit.Experimental.UI.NonNativeKeyboard _keyboardInstance;
        [Header("UI References")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private RectTransform contentContainer;
        [SerializeField] private Button confirmButton;
        [SerializeField] private Button cancelButton;
        [SerializeField] private GameObject keyboardPrefab;

        [Header("Prefabs")]
        [SerializeField] private GameObject sliderFieldPrefab;
        [SerializeField] private GameObject inputFieldPrefab;
        [SerializeField] private GameObject toggleFieldPrefab;
        [SerializeField] private GameObject vector3FieldPrefab;
        [SerializeField] private GameObject colorFieldPrefab;
        [SerializeField] private GameObject enumFieldPrefab;

        public event Action OnPanelClosed;

        private BaseObject _targetObject;
        private GhostController _ghostController;
        private List<FieldInfo> _editableFields = new List<FieldInfo>();
        private Dictionary<FieldInfo, object> _pendingValues = new Dictionary<FieldInfo, object>();
        private Dictionary<FieldInfo, object> _originalValues = new Dictionary<FieldInfo, object>();
        private Action<string> _currentKeyboardCallback;

        private void Awake()
        {
            if (confirmButton != null) confirmButton.onClick.AddListener(OnConfirm);
            if (cancelButton != null) cancelButton.onClick.AddListener(OnCancel);
            gameObject.SetActive(false);
        }

        public void Show(BaseObject target, Vector3 position, GhostController ghost)
        {
            if (target == null || target.Config == null)
            {
                Debug.LogWarning("[ObjectInspectorPanel] Invalid target or missing config.");
                return;
            }

            _targetObject = target;
            _ghostController = ghost;
            _editableFields = ParameterReflector.GetEditableFields(target.Config);

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
                titleText.text = $"Edit: {target.Config.name}";
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

                object defaultValue = field.GetValue(target.Config);
                object currentValue = ParameterReflector.GetOverriddenValue(target, field.Name, field.FieldType, defaultValue);
                
                _originalValues[field] = currentValue;
                _pendingValues[field] = currentValue;

                CreateFieldUI(field, currentValue);
            }

            gameObject.SetActive(true);
            _targetObject.ShowRuntimePreview = true;
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

                if (valueText != null) valueText.text = floatValue.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

                slider.onValueChanged.AddListener((v) =>
                {
                    object val = type == typeof(int) ? (int)v : v;
                    _pendingValues[field] = val;
                    if (valueText != null) valueText.text = v.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                    ApplyOverride(field, val); // Live update
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
                    ApplyOverride(field, v); // Live update
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
                    ApplyOverride(field, v); // Live update
                    UpdateGhostPreview();
                });

                // VR Keyboard Integration
                inputField.onSelect.AddListener((v) => OpenKeyboard(inputField, (text) => 
                {
                    inputField.text = text;
                    _pendingValues[field] = text;
                }));
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
            if (vector3FieldPrefab == null)
            {
                // Fallback to single input field with comma separation
                if (inputFieldPrefab == null) return;
                GameObject go = Instantiate(inputFieldPrefab, contentContainer);
                var labelText = go.GetComponentInChildren<TMP_Text>();
                if (labelText != null) labelText.text = label;
                var inputField = go.GetComponentInChildren<TMP_InputField>();
                inputField.text = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F2}, {1:F2}, {2:F2}", value.x, value.y, value.z);

                inputField.onValueChanged.AddListener((v) =>
                {
                    Vector3 parsed = ParseVector3(v);
                    object finalVal = isQuaternion ? (object)Quaternion.Euler(parsed) : (object)parsed;
                    _pendingValues[field] = finalVal;
                    ApplyOverride(field, finalVal);
                    UpdateGhostPreview();
                });
                
                // Keyboard support for fallback
                inputField.onSelect.AddListener((v) => OpenKeyboard(inputField, (text) => 
                {
                    inputField.text = text;
                    // Value parsing happens in onValueChanged
                }));
                return;
            }

            // Specific Vector3 prefab expected to have 3 TMP_InputFields
            GameObject vectorGo = Instantiate(vector3FieldPrefab, contentContainer);
            var vLabel = vectorGo.GetComponentInChildren<TMP_Text>();
            if (vLabel != null) vLabel.text = label;

            var inputs = vectorGo.GetComponentsInChildren<TMP_InputField>();
            if (inputs.Length >= 3)
            {
                inputs[0].text = value.x.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                inputs[1].text = value.y.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                inputs[2].text = value.z.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

                for (int i = 0; i < 3; i++)
                {
                    int index = i;
                    inputs[i].onValueChanged.AddListener((v) =>
                    {
                        Vector3 current = _pendingValues.ContainsKey(field) ? (isQuaternion ? ((Quaternion)_pendingValues[field]).eulerAngles : (Vector3)_pendingValues[field]) : value;
                        // Replace comma with dot and use NumberStyles.Float to avoid "1,00" -> 100 (thousands separator)
                        string safeVal = v.Replace(',', '.');
                        if (float.TryParse(safeVal, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f))
                        {
                            if (index == 0) current.x = f;
                            else if (index == 1) current.y = f;
                            else if (index == 2) current.z = f;
                            
                            object finalVal = isQuaternion ? (object)Quaternion.Euler(current) : (object)current;
                            _pendingValues[field] = finalVal;
                            ApplyOverride(field, finalVal);
                            UpdateGhostPreview();
                        }
                    });
                    
                    // Keyboard support
                    inputs[i].onSelect.AddListener((v) => OpenKeyboard(inputs[index], (text) => inputs[index].text = text));
                }
            }
        }

        private void CreateColorUI(FieldInfo field, string label, Color value)
        {
            if (colorFieldPrefab != null)
            {
                GameObject colorGo = Instantiate(colorFieldPrefab, contentContainer);
                var cLabel = colorGo.GetComponentInChildren<TMP_Text>();
                if (cLabel != null) cLabel.text = label;

                var sliders = colorGo.GetComponentsInChildren<Slider>();
                if (sliders.Length >= 4)
                {
                    float[] channels = { value.r, value.g, value.b, value.a };
                    for (int i = 0; i < 4; i++)
                    {
                        int idx = i;
                        sliders[i].value = channels[i];
                        sliders[i].onValueChanged.AddListener((v) =>
                        {
                            Color c = _pendingValues.ContainsKey(field) ? (Color)_pendingValues[field] : value;
                            if (idx == 0) c.r = v;
                            else if (idx == 1) c.g = v;
                            else if (idx == 2) c.b = v;
                            else if (idx == 3) c.a = v;
                            _pendingValues[field] = c;
                            ApplyOverride(field, c);
                            UpdateGhostPreview();
                        });
                    }
                    return;
                }
            }

            // Fallback to 4 independent sliders
            if (sliderFieldPrefab == null) return;

            string[] channelLabels = { "R", "G", "B", "A" };
            float[] initialValues = { value.r, value.g, value.b, value.a };

            for (int i = 0; i < 4; i++)
            {
                int channelIndex = i;
                GameObject go = Instantiate(sliderFieldPrefab, contentContainer);
                var labelText = go.GetComponentInChildren<TMP_Text>();
                var slider = go.GetComponentInChildren<Slider>();
                var valueText = go.transform.Find("ValueText")?.GetComponent<TMP_Text>();

                if (labelText != null) labelText.text = $"{label} {channelLabels[i]}";

                slider.minValue = 0f;
                slider.maxValue = 1f;
                slider.value = initialValues[i];
                if (valueText != null) valueText.text = initialValues[i].ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

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
                    if (valueText != null) valueText.text = v.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                    ApplyOverride(field, c);
                    UpdateGhostPreview();
                });
            }
        }

        private void CreateEnumUI(FieldInfo field, string label, object value, Type enumType)
        {
            if (enumFieldPrefab != null)
            {
                GameObject enumGo = Instantiate(enumFieldPrefab, contentContainer);
                var eLabel = enumGo.GetComponentInChildren<TMP_Text>();
                if (eLabel != null) eLabel.text = label;

                var dropdown = enumGo.GetComponentInChildren<TMP_Dropdown>();
                if (dropdown != null)
                {
                    string[] names = Enum.GetNames(enumType);
                    dropdown.ClearOptions();
                    dropdown.AddOptions(new List<string>(names));
                    dropdown.value = Array.IndexOf(names, value.ToString());

                    dropdown.onValueChanged.AddListener((idx) =>
                    {
                        object parsed = Enum.Parse(enumType, names[idx]);
                        _pendingValues[field] = parsed;
                        ApplyOverride(field, parsed);
                        UpdateGhostPreview();
                    });
                    return;
                }
            }

            if (inputFieldPrefab == null) return;

            GameObject go = Instantiate(inputFieldPrefab, contentContainer);
            var labelText = go.GetComponentInChildren<TMP_Text>();
            var inputField = go.GetComponentInChildren<TMP_InputField>();

            // For now, use a simple text input. A dropdown would be ideal but requires a new prefab.
            if (labelText != null) labelText.text = label;
            inputField.text = value.ToString();

            // Show available options as placeholder
            string[] options = Enum.GetNames(enumType);
            inputField.placeholder.GetComponent<TMP_Text>().text = string.Join(", ", options);

            inputField.onValueChanged.AddListener((v) =>
            {
                try
                {
                    object parsed = Enum.Parse(enumType, v, true);
                    _pendingValues[field] = parsed;
                    ApplyOverride(field, parsed); // Live update
                    UpdateGhostPreview();
                }
                catch { /* Invalid enum value, ignore */ }
            });
        }

        private Vector3 ParseVector3(string input)
        {
            try
            {
                // If the user uses commas as decimals AND as separators, this is tricky.
                // We assume either "1.0, 2.0, 3.0" or "1,0; 2,0; 3,0". 
                // Let's try to be smart: replace semi-colons with commas first, then split.
                string safeInput = input.Replace(';', ',');
                string[] parts = safeInput.Split(',');
                
                // If we have more than 3 parts, it's likely "1,0, 2,0, 3,0" -> we should probably have used specialized prefabs.
                // But let's try to handle 3 values.
                List<float> foundFloats = new List<float>();
                foreach(var p in parts)
                {
                    string s = p.Trim().Replace(',', '.');
                    if (float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f))
                    {
                        foundFloats.Add(f);
                    }
                }

                if (foundFloats.Count >= 3)
                {
                    return new Vector3(foundFloats[0], foundFloats[1], foundFloats[2]);
                }
            }
            catch { }
            return Vector3.zero;
        }

        private void Update()
        {
            if (_keyboardInstance != null && _keyboardInstance.gameObject.activeSelf)
            {
                Transform cam = Camera.main.transform;
                // Place slightly in front of the panel or camera
                Vector3 targetPos = transform.position + transform.forward * -0.3f + transform.up * -0.1f;
                
                // Smooth follow
                _keyboardInstance.transform.position = Vector3.Lerp(_keyboardInstance.transform.position, targetPos, Time.deltaTime * 5f);
                _keyboardInstance.transform.LookAt(cam);
                _keyboardInstance.transform.Rotate(0, 180, 0); 
            }
        }

        private void OpenKeyboard(TMP_InputField target, Action<string> onTextUpdated)
        {
            _keyboardInstance = Microsoft.MixedReality.Toolkit.Experimental.UI.NonNativeKeyboard.Instance;
            
            if (_keyboardInstance == null && keyboardPrefab != null)
            {
                GameObject go = Instantiate(keyboardPrefab);
                _keyboardInstance = go.GetComponent<Microsoft.MixedReality.Toolkit.Experimental.UI.NonNativeKeyboard>();
            }

            if (_keyboardInstance == null) return;

            // Unsubscribe previous to avoid mixed signals
            _keyboardInstance.OnTextUpdated -= HandleKeyboardTextUpdated;
            _keyboardInstance.OnClosed -= HandleKeyboardClosed;
            _keyboardInstance.OnTextSubmitted -= HandleKeyboardClosed;

            _currentKeyboardCallback = onTextUpdated;

            _keyboardInstance.OnTextUpdated += HandleKeyboardTextUpdated;
            _keyboardInstance.OnClosed += HandleKeyboardClosed;
            _keyboardInstance.OnTextSubmitted += HandleKeyboardClosed;

            _keyboardInstance.PresentKeyboard(target.text);
            
            // Positioning: Snap immediately
            Vector3 targetPos = transform.position + transform.forward * -0.3f + transform.up * -0.1f;
            _keyboardInstance.transform.position = targetPos;
            _keyboardInstance.transform.LookAt(Camera.main.transform);
            _keyboardInstance.transform.Rotate(0, 180, 0); 
        }

        private void HandleKeyboardTextUpdated(string text)
        {
            _currentKeyboardCallback?.Invoke(text);
        }

        private void HandleKeyboardClosed(object sender, EventArgs e)
        {
            _currentKeyboardCallback = null;
        }

        private void UpdateGhostPreview()
        {
            if (_targetObject == null) return;

            // Trigger the runtime preview system to redraw with new pending values
            // (BaseObject.LateUpdate handles the actual drawing)
            if (_ghostController != null)
            {
                _ghostController.Show(_targetObject.gameObject, _targetObject.transform.position, _targetObject.transform.rotation);
                _ghostController.SetValid(true);
            }
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
            // Revert to original values
            foreach (var kvp in _originalValues)
            {
                ApplyOverride(kvp.Key, kvp.Value);
            }
            Close();
        }

        private void Close()
        {
            if (_targetObject != null)
            {
                _targetObject.ShowRuntimePreview = false;
            }

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

            string serializedValue = ParameterReflector.SerializeValue(value);
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
            
            // Instead, force the object to refresh its state using the new overrides
            _targetObject.SyncAllColliders();
            _targetObject.SyncVisualOffset();
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
