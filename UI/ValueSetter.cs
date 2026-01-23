using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UI;

public class ValueSetter : MonoBehaviour
{
    private static CommonData commonData = CommonData.Instance;

    public string valueName;
    [HideInInspector] public TextMeshProUGUI valueNameText;
    [HideInInspector] public TMP_InputField valueInputField;

    void Start()
    {
        valueNameText = CommonData.FindInChild(gameObject, "Name").GetComponent<TextMeshProUGUI>();
        valueInputField = CommonData.FindInChild(gameObject, "InputField").GetComponent<TMP_InputField>();
        Assert.IsNotNull(valueNameText);
        Assert.IsNotNull(valueInputField);

        valueNameText.text = valueName;
        // valueNameInputField.onEndEdit.AddListener(OnValueChanged);
    }

    public double GetValue()
    {
        return double.Parse(valueInputField.text);
    }

    // void Update() { }
}
