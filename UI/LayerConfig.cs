using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UI;

public class LayerConfig : MonoBehaviour
{
    private static CommonData commonData = CommonData.Instance;
    private LayeredSeismicPropagator.Data data = new LayeredSeismicPropagator.Data();
    private LayeredSeismicPropagator propagator = new LayeredSeismicPropagator();

    [SerializeField] private ConfigMenu configMenu;

    [SerializeField] private Button exitButton;
    [SerializeField] private Button applyButton;

    [SerializeField] private TMP_InputField NL0;
    [SerializeField] private TMP_InputField NR;
    [SerializeField] private TMP_InputField ZF0;
    [SerializeField] private TMP_InputField ZF1;
    [SerializeField] private TMP_InputField ZH;
    [SerializeField] private TMP_InputField STRIKE;
    [SerializeField] private TMP_InputField DIP;
    [SerializeField] private TMP_InputField RAKE;
    [SerializeField] private TMP_InputField VR;
    [SerializeField] private TMP_InputField SLIP;
    [SerializeField] private TMP_InputField FAULTL1;
    [SerializeField] private TMP_InputField FAULTL2;
    [SerializeField] private TMP_InputField NX;
    [SerializeField] private TMP_InputField NZ;
    [SerializeField] private TMP_InputField NTIME;
    [SerializeField] private TMP_InputField TL;
    [SerializeField] private TMP_InputField TSOURCE;

    [SerializeField] private TMP_InputField layers;
    [SerializeField] private TMP_InputField receivers;

    [SerializeField] private TextMeshProUGUI MwText;

    void Awake()
    {
        commonData.RegisterUI("LayerConfig", gameObject);
        commonData.EnableUI("LayerConfig", false);
    }

    void Start()
    {
        exitButton.onClick.AddListener(OnExitButtonClick);
        applyButton.onClick.AddListener(OnApplyButtonClick);

        UpdateMwText();
        TMP_InputField[] mwFields = new TMP_InputField[] {
            NL0, NR, ZF0, ZF1, ZH, STRIKE, DIP, RAKE,
            VR, SLIP, FAULTL1, FAULTL2, NX, NZ, NTIME,
            TL, TSOURCE,
        };
        foreach (TMP_InputField field in mwFields)
        {
            field.onEndEdit.AddListener(_ => UpdateMwText());
        }
    }

    void OnExitButtonClick()
    {
        commonData.EnableUI("LayerConfig", false);
        commonData.EnableUI("ConfigMenu", true);
    }

    private bool ParseInput()
    {
        if (!int.TryParse(NL0.text, out data.NL0) || data.NL0 < 1 || data.NL0 > LayeredSeismicPropagator.NLMAX)
        {
            Debug.LogWarning("Error parsing NL0. Must be between 1 and " + LayeredSeismicPropagator.NLMAX);
            return false;
        }
        if (!int.TryParse(NR.text, out data.NR) || data.NR < 1 || data.NR > LayeredSeismicPropagator.NRMAX)
        {
            Debug.LogWarning("Error parsing NR. Must be between 1 and " + LayeredSeismicPropagator.NRMAX);
            return false;
        }
        if (!float.TryParse(ZF0.text, out data.ZF0))
        {
            Debug.LogWarning("Error parsing ZF0.");
            return false;
        }
        if (!float.TryParse(ZF1.text, out data.ZF1))
        {
            Debug.LogWarning("Error parsing ZF1.");
            return false;
        }
        if (!float.TryParse(ZH.text, out data.ZH))
        {
            Debug.LogWarning("Error parsing ZH.");
            return false;
        }
        if (!float.TryParse(STRIKE.text, out data.STRIKE))
        {
            Debug.LogWarning("Error parsing STRIKE.");
            return false;
        }
        if (!float.TryParse(DIP.text, out data.DIP))
        {
            Debug.LogWarning("Error parsing DIP.");
            return false;
        }
        if (!float.TryParse(RAKE.text, out data.RAKE))
        {
            Debug.LogWarning("Error parsing RAKE.");
            return false;
        }
        if (!float.TryParse(VR.text, out data.VR))
        {
            Debug.LogWarning("Error parsing VR.");
            return false;
        }
        if (!float.TryParse(SLIP.text, out data.SLIP))
        {
            Debug.LogWarning("Error parsing SLIP.");
            return false;
        }
        if (!float.TryParse(FAULTL1.text, out data.FAULTL1))
        {
            Debug.LogWarning("Error parsing FAULTL1.");
            return false;
        }
        if (!float.TryParse(FAULTL2.text, out data.FAULTL2))
        {
            Debug.LogWarning("Error parsing FAULTL2.");
            return false;
        }
        if (!int.TryParse(NX.text, out data.NX) || data.NX < 1 || data.NX > LayeredSeismicPropagator.NXMAX)
        {
            Debug.LogWarning("Error parsing NX. Must be between 1 and " + LayeredSeismicPropagator.NXMAX);
            return false;
        }
        if (!int.TryParse(NZ.text, out data.NZ) || data.NZ < 1 || data.NZ > LayeredSeismicPropagator.NZMAX)
        {
            Debug.LogWarning("Error parsing NZ. Must be between 1 and " + LayeredSeismicPropagator.NZMAX);
            return false;
        }
        if (!int.TryParse(NTIME.text, out data.NTIME))
        {
            Debug.LogWarning("Error parsing NTIME.");
            return false;
        }
        if (!float.TryParse(TL.text, out data.TL))
        {
            Debug.LogWarning("Error parsing TL.");
            return false;
        }
        if (!float.TryParse(TSOURCE.text, out data.TSOURCE))
        {
            Debug.LogWarning("Error parsing TSOURCE.");
            return false;
        }

        {
            // TH, AL0, BE0, DENS, QP, QS
            data.TH = new float[data.NL0];
            data.AL0 = new float[data.NL0];
            data.BE0 = new float[data.NL0];
            data.DENS = new float[data.NL0];
            data.QP = new float[data.NL0];
            data.QS = new float[data.NL0];

            string[] layerLines = layers.text.Split(new[] { '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (layerLines.Length != data.NL0)
            {
                Debug.LogWarning($"Layer input must have exactly {data.NL0} lines.");
                return false;
            }
            for (int i = 0; i < data.NL0; i++)
            {
                string[] values = layerLines[i].Trim().Split(new[] { ' ', '\t', ',' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (values.Length != 6)
                {
                    Debug.LogWarning($"Line {i + 1} in layer input does not have 6 values.");
                    return false;
                }
                if (!float.TryParse(values[0], out data.TH[i]) ||
                    !float.TryParse(values[1], out data.AL0[i]) ||
                    !float.TryParse(values[2], out data.BE0[i]) ||
                    !float.TryParse(values[3], out data.DENS[i]) ||
                    !float.TryParse(values[4], out data.QP[i]) ||
                    !float.TryParse(values[5], out data.QS[i]))
                {
                    Debug.LogWarning($"Error parsing values in line {i + 1} of layer input.");
                    return false;
                }
            }
        }

        {
            // R0 AZ
            data.R0 = new float[data.NR];
            data.AZ = new float[data.NR];

            string[] receiverLines = receivers.text.Split(new[] { '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (receiverLines.Length != data.NR)
            {
                Debug.LogWarning($"Receiver input must have exactly {data.NR} lines.");
                return false;
            }
            for (int i = 0; i < data.NR; i++)
            {
                string[] values = receiverLines[i].Trim().Split(new[] { ' ', '\t', ',' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (values.Length != 2)
                {
                    Debug.LogWarning($"Line {i + 1} in receiver input does not have 2 values.");
                    return false;
                }
                if (!float.TryParse(values[0], out data.R0[i]) ||
                    !float.TryParse(values[1], out data.AZ[i]))
                {
                    Debug.LogWarning($"Error parsing values in line {i + 1} of receiver input.");
                    return false;
                }
            }
        }

        return true;
    }

    void UpdateMwText()
    {
        if (!ParseInput())
        {
            MwText.text = "Mw = Error parsing input";
            return;
        }
        // 地震矩 M0 (单位dyn*cm=10^-7 N*m)
        //      = 3.3e11 (dyn/cm^2, 地震发生断层附近岩石平均剪切模量)
        //      * 1e10
        //      * length (km)
        //      * width (km)
        //      * slip (cm)
        // 震级 Mw = 2/3log10(M0) - 10.7
        // 1 dyn = 10^-5 N

        double length = data.FAULTL1 + data.FAULTL2; // km
        double width = data.ZF1 - data.ZF0; // km
        double M0 = 3.3e11 * 1e10 * length * width * data.SLIP;
        double Mw = 2.0 / 3.0 * Math.Log10(M0) - 10.7;
        MwText.text = "Mw = " + Mw.ToString("F2");
    }

    private IEnumerator RunLSP()
    {
        yield return propagator.RunGPUCoroutine();
        configMenu.RefreshSeismicDataDropdown();
    }

    public void OnApplyButtonClick()
    {
        if (!ParseInput()) return;
        propagator.SetData(data);
        StartCoroutine(RunLSP());
    }

    // void Update()
    // {
    //     if (Input.GetKeyDown(KeyCode.C))
    //     {
    //         propagator.Run();
    //     }
    // }
}
