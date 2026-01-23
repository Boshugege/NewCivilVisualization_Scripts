using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;
using static MathNet.Numerics.SpecialFunctions;
using Debug = UnityEngine.Debug;

public struct Matrix2x2
{
    public Complex a00, a01, a10, a11;

    public static readonly Matrix2x2 Zero = new Matrix2x2(0, 0, 0, 0);
    public static readonly Matrix2x2 Identity = new Matrix2x2(1, 0, 0, 1);

    public Matrix2x2(Complex a00, Complex a01, Complex a10, Complex a11)
    {
        this.a00 = a00;
        this.a01 = a01;
        this.a10 = a10;
        this.a11 = a11;
    }

    public Complex this[int i, int j]
    {
        get
        {
            if (i == 0 && j == 0) return a00;
            if (i == 0 && j == 1) return a01;
            if (i == 1 && j == 0) return a10;
            if (i == 1 && j == 1) return a11;
            throw new IndexOutOfRangeException("Invalid matrix index.");
        }
        set
        {
            if (i == 0 && j == 0) a00 = value;
            else if (i == 0 && j == 1) a01 = value;
            else if (i == 1 && j == 0) a10 = value;
            else if (i == 1 && j == 1) a11 = value;
            else throw new IndexOutOfRangeException("Invalid matrix index.");
        }
    }

    public static Matrix2x2 Diag(Complex a)
    {
        return new Matrix2x2(a, 0, 0, a);
    }

    public static Matrix2x2 Inverse(Matrix2x2 m)
    {
        Complex det = m.a00 * m.a11 - m.a01 * m.a10;
        return new Matrix2x2(m.a11 / det, -m.a01 / det, -m.a10 / det, m.a00 / det);
    }

    public static Matrix2x2 operator *(Matrix2x2 m1, Matrix2x2 m2)
    {
        return new Matrix2x2(
            m1.a00 * m2.a00 + m1.a01 * m2.a10,
            m1.a00 * m2.a01 + m1.a01 * m2.a11,
            m1.a10 * m2.a00 + m1.a11 * m2.a10,
            m1.a10 * m2.a01 + m1.a11 * m2.a11
        );
    }

    public static Matrix2x2 operator *(Matrix2x2 m, Complex c)
    {
        return new Matrix2x2(m.a00 * c, m.a01 * c, m.a10 * c, m.a11 * c);
    }

    public static Matrix2x2 operator +(Matrix2x2 m1, Matrix2x2 m2)
    {
        return new Matrix2x2(m1.a00 + m2.a00, m1.a01 + m2.a01, m1.a10 + m2.a10, m1.a11 + m2.a11);
    }

    public static Matrix2x2 operator -(Matrix2x2 m1, Matrix2x2 m2)
    {
        return new Matrix2x2(m1.a00 - m2.a00, m1.a01 - m2.a01, m1.a10 - m2.a10, m1.a11 - m2.a11);
    }

    public static Matrix2x2 operator -(Matrix2x2 m)
    {
        return new Matrix2x2(-m.a00, -m.a01, -m.a10, -m.a11);
    }

    public static Matrix2x2 operator /(Matrix2x2 m, Complex c)
    {
        return new Matrix2x2(m.a00 / c, m.a01 / c, m.a10 / c, m.a11 / c);
    }

    public static Matrix2x2 operator /(Matrix2x2 m1, Matrix2x2 m2)
    {
        Complex det = m2.a00 * m2.a11 - m2.a01 * m2.a10;
        return new Matrix2x2(
            (m1.a00 * m2.a11 - m1.a01 * m2.a10) / det,
            (m1.a01 * m2.a00 - m1.a00 * m2.a01) / det,
            (m1.a10 * m2.a11 - m1.a11 * m2.a10) / det,
            (m1.a11 * m2.a00 - m1.a10 * m2.a01) / det
        );
    }
}

public class LayeredSeismicPropagator
{
    public struct Data
    {
        public int NL0, NR, NX, NZ, NTIME;
        public float ZF0, ZF1, ZH, STRIKE, DIP, RAKE, VR, SLIP, FAULTL1, FAULTL2, TL, TSOURCE;
        public float[] TH, AL0, BE0, DENS, QP, QS, R0, AZ;
    }

    // 定义常量
    public const int NLMAX = 25;
    public const int NRMAX = 12;
    public const int NTMAX = 8192;
    public const int NXMAX = 50;
    public const int NZMAX = 10;
    public const int MMAX = 2000;
    public const int NLMX = NLMAX + 2 + NZMAX;
    public const float EPS = 1E-3f;

    // ReadInputData() 中读写，之外只读
    private int NL0, NR, NX, NZ, NTIME;
    private float ZF0, ZF1, ZH, STRIKE, DIP, RAKE, VR, SLIP, FAULTL1, FAULTL2, TL, TSOURCE;
    private bool RT = false;
    private float[] R0 = new float[NRMAX];
    private float[] AZ = new float[NRMAX];

    // CalculateParameters() 中读写，之外只读
    private int M, NL, NFREQ;
    private float CM11, CM22, CM33, CM12, CM13, CM23, DFREQ, AW, DL, DZ, XL, SDI, PIL, DT;
    private float[,,] S2T = new float[NXMAX, NZMAX, NRMAX];
    private float[,,] C2T = new float[NXMAX, NZMAX, NRMAX];
    private float[,,] ST = new float[NXMAX, NZMAX, NRMAX];
    private float[,,] CT = new float[NXMAX, NZMAX, NRMAX];
    private float[,,,] AJ0 = new float[NXMAX, NZMAX, NRMAX, MMAX];
    private float[,,,] AJ1 = new float[NXMAX, NZMAX, NRMAX, MMAX];
    private float[,,] R = new float[NXMAX, NZMAX, NRMAX];
    private Complex[] CTH = new Complex[NLMX];
    private Complex[] AQP = new Complex[NLMX];
    private Complex[] AQS = new Complex[NLMX];
    private Complex[,] EV = new Complex[NXMAX, NZMAX];
    private Complex[,] TEV = new Complex[NXMAX, NZMAX];
    private int[] LAY = new int[NZMAX];

    // ReadInputData() & CalculateParameters() 中读写，之外只读
    private float[] TH = new float[NLMX];
    private float[] AL0 = new float[NLMX];
    private float[] BE0 = new float[NLMX];
    private float[] DENS = new float[NLMX];
    private float[] QP = new float[NLMX];
    private float[] QS = new float[NLMX];

    // CalculateFrequency() 中读写，之外只读
    private Complex[,,] U = new Complex[NTMAX / 2, NRMAX, 3];
    private Complex[,] SOURCE = new Complex[NTMAX / 2, 3];

    // 用于 GPU 计算
    private ComputeShader computeShader;
    private int kernelHandle;
    ComputeBuffer AL0Buffer;
    ComputeBuffer BE0Buffer;
    ComputeBuffer DENSBuffer;
    ComputeBuffer QPBuffer;
    ComputeBuffer QSBuffer;
    ComputeBuffer AQPBuffer;
    ComputeBuffer AQSBuffer;
    ComputeBuffer CTHBuffer;
    ComputeBuffer LAYBuffer;
    ComputeBuffer EVBuffer;
    ComputeBuffer TEVBuffer;
    ComputeBuffer RBuffer;
    ComputeBuffer STBuffer;
    ComputeBuffer CTBuffer;
    ComputeBuffer S2TBuffer;
    ComputeBuffer C2TBuffer;
    ComputeBuffer AJ0Buffer;
    ComputeBuffer AJ1Buffer;

    ComputeBuffer UBuffer;
    ComputeBuffer SOURCEBuffer;
    ComputeBuffer MatrixesBuffer;
    ComputeBuffer ComplexesBuffer;

    public LayeredSeismicPropagator() { }

    ~LayeredSeismicPropagator() { }

    private void ReadInputData()
    {
        using StreamReader inputFile = new StreamReader($"{Application.streamingAssetsPath}/Python/seismic_data/MOD_input");

        Func<string[]> ReadLine = () => inputFile.ReadLine()?.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();

        string[] vars;
        // 读取输入数据
        NL0 = int.Parse(ReadLine()[0]);
        for (int L = 0; L < NL0; L++)
        {
            vars = ReadLine();
            TH[L] = float.Parse(vars[0]);
            AL0[L] = float.Parse(vars[1]);
            BE0[L] = float.Parse(vars[2]);
            DENS[L] = float.Parse(vars[3]);
            QP[L] = float.Parse(vars[4]);
            QS[L] = float.Parse(vars[5]);
        }

        // 读取其他参数
        vars = ReadLine();
        ZF0 = float.Parse(vars[0]);
        ZF1 = float.Parse(vars[1]);
        ZH = float.Parse(vars[2]);

        vars = ReadLine();
        STRIKE = float.Parse(vars[0]);
        DIP = float.Parse(vars[1]);
        RAKE = float.Parse(vars[2]);

        VR = float.Parse(ReadLine()[0]);
        SLIP = float.Parse(ReadLine()[0]);

        vars = ReadLine();
        FAULTL1 = float.Parse(vars[0]);
        FAULTL2 = float.Parse(vars[1]);

        NR = int.Parse(ReadLine()[0]);
        for (int IR = 0; IR < NR; IR++)
        {
            vars = ReadLine();
            R0[IR] = float.Parse(vars[0]);
            AZ[IR] = float.Parse(vars[1]);
        }

        vars = ReadLine();
        NX = int.Parse(vars[0]);
        NZ = int.Parse(vars[1]);

        vars = ReadLine();
        NTIME = int.Parse(vars[0]);
        TL = float.Parse(vars[1]);

        TSOURCE = float.Parse(ReadLine()[0]);
    }

    private void InitializeComputeShader()
    {
        if (computeShader == null)
        {
            computeShader = Resources.Load<ComputeShader>("LayeredSeismicPropagator");
            Assert.IsNotNull(computeShader, "Compute Shader 'LayeredSeismicPropagator' not found in Resources.");
            kernelHandle = computeShader.FindKernel("CalculateFrequency");
        }
    }

    public void SetData(Data data)
    {
        NL0 = data.NL0;
        NR = data.NR;
        NX = data.NX;
        NZ = data.NZ;
        NTIME = data.NTIME;
        ZF0 = data.ZF0;
        ZF1 = data.ZF1;
        ZH = data.ZH;
        STRIKE = data.STRIKE;
        DIP = data.DIP;
        RAKE = data.RAKE;
        VR = data.VR;
        SLIP = data.SLIP;
        FAULTL1 = data.FAULTL1;
        FAULTL2 = data.FAULTL2;
        TL = data.TL;
        TSOURCE = data.TSOURCE;

        Array.Copy(data.TH, TH, NL0);
        Array.Copy(data.AL0, AL0, NL0);
        Array.Copy(data.BE0, BE0, NL0);
        Array.Copy(data.DENS, DENS, NL0);
        Array.Copy(data.QP, QP, NL0);
        Array.Copy(data.QS, QS, NL0);
        Array.Copy(data.R0, R0, NR);
        Array.Copy(data.AZ, AZ, NR);
    }

    private void CalculateParameters()
    {
        float[] XR0 = new float[NRMAX];
        float[] YR0 = new float[NRMAX];

        // 计算参数
        M = MMAX - 1;
        XL = AL0[NL0 - 1] * TL;
        for (int IR = 0; IR < NR; IR++)
        {
            float XX = AL0[NL0 - 1] * TL + R0[IR];
            if (XX > XL) XL = XX;
        }

        // 计算接收器坐标
        for (int IR = 0; IR < NR; IR++)
        {
            XR0[IR] = (float)(R0[IR] * Math.Cos(AZ[IR] * Math.PI / 180.0));
            YR0[IR] = (float)(R0[IR] * Math.Sin(AZ[IR] * Math.PI / 180.0));
        }

        // 计算断层参数
        float CS = (float)Math.Cos(STRIKE * Math.PI / 180.0);
        float SS = (float)Math.Sin(STRIKE * Math.PI / 180.0);
        float CDI = (float)Math.Cos(DIP * Math.PI / 180.0);
        SDI = (float)Math.Sin(DIP * Math.PI / 180.0);
        float CR = (float)Math.Cos(RAKE * Math.PI / 180.0);
        float SR = (float)Math.Sin(RAKE * Math.PI / 180.0);

        float AS1 = CR * CS + SR * CDI * SS;
        float AS2 = CR * SS - SR * CDI * CS;
        float AS3 = -SR * SDI;
        float AN1 = -SDI * SS;
        float AN2 = SDI * CS;
        float AN3 = -CDI;

        CM11 = (float)(-2.0 * AS1 * AN1);
        CM22 = (float)(-2.0 * AS2 * AN2);
        CM33 = (float)(-2.0 * AS3 * AN3);
        CM12 = -(AS1 * AN2 + AS2 * AN1);
        CM13 = -(AS1 * AN3 + AS3 * AN1);
        CM23 = -(AS2 * AN3 + AS3 * AN2);

        // 添加上层半空间
        NL = NL0 + 1;
        for (int L = NL - 1; L > 0; L--)
        {
            TH[L] = TH[L - 1];
            AL0[L] = AL0[L - 1];
            BE0[L] = BE0[L - 1];
            DENS[L] = DENS[L - 1];
            QP[L] = QP[L - 1];
            QS[L] = QS[L - 1];
        }
        TH[NL - 1] = 0.0f;

        // 添加空气层
        TH[0] = 0.0f;
        AL0[0] = 0.340f;
        BE0[0] = 0.001f;
        DENS[0] = 1.3E-3f;
        QP[0] = 1E6f;
        QS[0] = 1E6f;

        // 计算断层网格
        DZ = (ZF1 - ZF0) / NZ;
        float Z0 = (float)(ZF0 + DZ / 2.0);

        DL = (FAULTL1 + FAULTL2) / NX;
        float DX = DL * CS;
        float DY = DL * SS;
        float DL2 = (float)(DL / 2.0);
        float X0 = -(FAULTL1 - DL2) * CS;
        float Y0 = -(FAULTL1 - DL2) * SS;

        // 计算层位
        for (int IZ = 0; IZ < NZ; IZ++)
        {
            float ZZ = Z0 + DZ * IZ;
            float ZT = 0.0f;
            float ZB = 0.0f;
            int L = 1;
            for (; L < NL; L++)
            {
                ZB = ZT + TH[L];
                ZT = ZB;
                if (ZZ <= ZB) break;
            }

            if (L == NL)
            {
                NL++;
                AL0[NL - 1] = AL0[NL - 2];
                BE0[NL - 1] = BE0[NL - 2];
                DENS[NL - 1] = DENS[NL - 2];
                QP[NL - 1] = QP[NL - 2];
                QS[NL - 1] = QS[NL - 2];
                TH[NL - 1] = 0.0f;
                TH[NL - 2] = ZB - ZZ;
                LAY[IZ] = NL - 1;
            }
            else
            {
                NL++;
                for (int LL = NL - 1; LL > L; LL--)
                {
                    TH[LL] = TH[LL - 1];
                    AL0[LL] = AL0[LL - 1];
                    BE0[LL] = BE0[LL - 1];
                    DENS[LL] = DENS[LL - 1];
                    QP[LL] = QP[LL - 1];
                    QS[LL] = QS[LL - 1];
                }
                TH[L + 1] = ZB - ZZ;
                TH[L] = TH[L] - TH[L + 1];
                LAY[IZ] = L + 1;
            }
        }

        // 计算频率参数
        PIL = (float)(2 * Math.PI / XL);
        DT = TL / NTIME;
        DFREQ = (float)(1.0 / TL);
        NFREQ = NTIME / 2;
        AW = (float)(-Math.PI / TL);

        // 计算衰减参数
        for (int L = 0; L < NL; L++)
        {
            AQP[L] = (1.0 + Complex.ImaginaryOne / (QP[L] + QP[L])) / (1.0 + 0.25 / (QP[L] * QP[L])) * AL0[L];
            AQS[L] = (1.0 + Complex.ImaginaryOne / (QS[L] + QS[L])) / (1.0 + 0.25 / (QS[L] * QS[L])) * BE0[L];
            CTH[L] = -Complex.ImaginaryOne * TH[L];
        }

        // 计算接收器位置
        for (int IR = 0; IR < NR; IR++)
        {
            for (int IX = 0; IX < NX; IX++)
            {
                for (int IZ = 0; IZ < NZ; IZ++)
                {
                    float ZZ = ZH - (Z0 + DZ * IZ);
                    float A1 = XR0[IR] - (X0 + DX * IX) - ZZ * CDI / SDI * SS;
                    float A2 = YR0[IR] - (Y0 + DY * IX) + ZZ * CDI / SDI * CS;
                    float E0 = (float)Math.Sqrt(A1 * A1 + A2 * A2);
                    if (E0 < DL2) E0 = DL2;
                    R[IX, IZ, IR] = E0;
                    float TETA = (float)(Math.Atan2(A2, A1) * 180.0 / Math.PI);
                    ST[IX, IZ, IR] = (float)Math.Sin(TETA * Math.PI / 180.0);
                    CT[IX, IZ, IR] = (float)Math.Cos(TETA * Math.PI / 180.0);
                    S2T[IX, IZ, IR] = (float)(2.0 * ST[IX, IZ, IR] * CT[IX, IZ, IR]);
                    C2T[IX, IZ, IR] = CT[IX, IZ, IR] * CT[IX, IZ, IR] - ST[IX, IZ, IR] * ST[IX, IZ, IR];
                }
            }
        }

        // 计算断层破裂延迟
        Complex DOM = -Complex.ImaginaryOne * DFREQ * 2 * Math.PI;
        for (int IZ = 0; IZ < NZ; IZ++)
        {
            for (int IX = 0; IX < NX; IX++)
            {
                float XX = FAULTL1 - DL2 - DL * IX;
                float ZZ = (Z0 + DZ * IZ - ZH) / SDI;
                float E0 = (float)(Math.Sqrt(XX * XX + ZZ * ZZ) / VR);
                EV[IX, IZ] = Complex.Exp(E0 * AW) * SLIP;
                Complex C0 = E0 * DOM;
                TEV[IX, IZ] = Complex.Exp(C0);
            }
        }

        // 计算Bessel函数
        for (int K = 0; K < M; K++)
            for (int IR = 0; IR < NR; IR++)
                for (int IX = 0; IX < NX; IX++)
                    for (int IZ = 0; IZ < NZ; IZ++)
                    {
                        float ARG = PIL * (K + 1) * R[IX, IZ, IR];
                        AJ0[IX, IZ, IR, K] = (float)BesselJ(0, ARG);
                        AJ1[IX, IZ, IR, K] = (float)BesselJ(1, ARG);
                    }
    }

    // 频率循环
    private void CalculateFrequency(int IF)
    {
        Matrix2x2[] RD = new Matrix2x2[NLMX];
        Matrix2x2[] RU = new Matrix2x2[NLMX];
        Matrix2x2[] TD = new Matrix2x2[NLMX];
        Matrix2x2[] TU = new Matrix2x2[NLMX];
        Matrix2x2[] MB = new Matrix2x2[NLMX];
        Matrix2x2[] MT = new Matrix2x2[NLMX];
        Matrix2x2[] NB = new Matrix2x2[NLMX];
        Matrix2x2[] NT = new Matrix2x2[NLMX];
        Matrix2x2[] QUU = new Matrix2x2[NLMX];
        Matrix2x2[] QUD = new Matrix2x2[NLMX];
        Matrix2x2[] TUP = new Matrix2x2[NLMX];
        Matrix2x2[] G = new Matrix2x2[NLMX];
        Matrix2x2[] EE = new Matrix2x2[NLMX];
        Matrix2x2 C;
        Matrix2x2 E;

        Complex[] RDSH = new Complex[NLMX];
        Complex[] RUSH = new Complex[NLMX];
        Complex[] TDSH = new Complex[NLMX];
        Complex[] TUSH = new Complex[NLMX];
        Complex[] MBSH = new Complex[NLMX];
        Complex[] MTSH = new Complex[NLMX];
        Complex[] NBSH = new Complex[NLMX];
        Complex[] NTSH = new Complex[NLMX];
        Complex[] QUUSH = new Complex[NLMX];
        Complex[] QUDSH = new Complex[NLMX];
        Complex[] TUPSH = new Complex[NLMX];
        Complex[] GSH = new Complex[NLMX];
        Complex[] EESH = new Complex[NLMX];
        Complex[] ALPHA = new Complex[NLMX];
        Complex[] BETA = new Complex[NLMX];
        Complex[,,] EJ1 = new Complex[NXMAX, NRMAX, NZMAX];
        Complex[,,] EJ2 = new Complex[NXMAX, NRMAX, NZMAX];
        Complex[,,] EJ3 = new Complex[NXMAX, NRMAX, NZMAX];
        Complex[] BS = new Complex[2];
        Complex[] BL = new Complex[2];
        Complex[,] SD = new Complex[3, 5];
        Complex[,] SU = new Complex[3, 5];
        Complex[] WOA = new Complex[NLMX];
        Complex[] WOB = new Complex[NLMX];
        Complex[] EMU = new Complex[NLMX];
        Complex[] EMU2 = new Complex[NLMX];
        Complex[] WA2 = new Complex[NLMX];
        Complex[] WB2 = new Complex[NLMX];
        Complex[] WZA = new Complex[NLMX];
        Complex[] WZB = new Complex[NLMX];
        Complex[,] DU1 = new Complex[NZMAX, 5];
        Complex[,] DU2 = new Complex[NZMAX, 5];
        Complex[,] DU3 = new Complex[NZMAX, 5];
        Complex[] AMPSV = new Complex[NZMAX];
        Complex[] AMSH = new Complex[NZMAX];

        float FREQ = DFREQ * IF;
        float RW = (float)(2 * Math.PI * FREQ);
        Complex OMEGA = new Complex(RW, AW);

        // 计算源函数
        float tstart = -TSOURCE;
        Complex c1 = Complex.Exp(OMEGA * Math.PI * TSOURCE / 4.0);
        SOURCE[IF, 0] = -Complex.ImaginaryOne * Math.PI * TSOURCE / 2.0 / (c1 - 1.0 / c1) * Complex.Exp(Complex.ImaginaryOne * OMEGA * tstart);
        SOURCE[IF, 1] = Complex.ImaginaryOne * OMEGA * SOURCE[IF, 0];

        // Acc
        SOURCE[IF, 2] = Complex.ImaginaryOne * OMEGA * SOURCE[IF, 1];

        // 计算频散
        float FREQ0 = 1.0f;
        float ZOM = (float)(Math.Sqrt(RW * RW + AW * AW) / (2 * Math.PI));
        float PPHI = (float)((IF == 0) ? -Math.PI / 2.0 : Math.Atan(AW / RW));
        Complex XLNF = (Complex.ImaginaryOne * PPHI + Math.Log(ZOM) - Math.Log(FREQ0)) / Math.PI;

        for (int L = 0; L < NL; L++)
        {
            ALPHA[L] = AQP[L] / (1.0 - XLNF / QP[L]);
            if (Math.Abs(FREQ) < 1e-10) ALPHA[L] = AL0[L];
            BETA[L] = AQS[L] / (1.0 - XLNF / QS[L]);
            if (Math.Abs(FREQ) < 1e-10) BETA[L] = BE0[L];
            EMU[L] = BETA[L] * BETA[L] * DENS[L];
            EMU2[L] = EMU[L] + EMU[L];
            WA2[L] = (OMEGA / ALPHA[L]) * (OMEGA / ALPHA[L]);
            WB2[L] = (OMEGA / BETA[L]) * (OMEGA / BETA[L]);
        }

        // 计算振幅
        for (int IZ = 0; IZ < NZ; IZ++)
        {
            int L = LAY[IZ];
            Complex A0 = DFREQ * EMU[L] * DL * (DZ / SDI) / (2.0 * XL * DENS[L]);
            AMPSV[IZ] = A0 / (OMEGA * OMEGA);
            AMSH[IZ] = A0 / (BETA[L] * BETA[L]);
        }

        // 初始化格林函数
        for (int IZ = 0; IZ < NZ; IZ++)
        {
            for (int IX = 0; IX < NX; IX++)
            {
                for (int IR = 0; IR < NR; IR++)
                {
                    EJ1[IX, IR, IZ] = Complex.Zero;
                    EJ2[IX, IR, IZ] = Complex.Zero;
                    EJ3[IX, IR, IZ] = Complex.Zero;
                }
            }
        }

        // 波数循环
        int NZK = NZ;
        int NLK = NL;
        int NLK1 = NLK - 1;
        int K;
        for (K = 0; K < M; K++)
        {
            float AK = PIL * (K + 1);
            float AK2 = AK * AK;
            Complex AIK = Complex.ImaginaryOne * AK;
            Complex AIK2 = Complex.ImaginaryOne * AK2;

            // 计算波数相关参数
            for (int L = 0; L < NLK; L++)
            {
                Complex C1 = WA2[L] - AK2;
                WZA[L] = Complex.Sqrt(C1);
                if (WZA[L].Imaginary > 0) WZA[L] = -WZA[L];
                Complex C2 = WB2[L] - AK2;
                WZB[L] = Complex.Sqrt(C2);
                if (WZB[L].Imaginary > 0) WZB[L] = -WZB[L];
                WOA[L] = WZA[L] / OMEGA;
                WOB[L] = WZB[L] / OMEGA;
            }

            // 计算反射和透射矩阵
            Complex CU = AK / OMEGA;
            Complex CU2 = CU * CU;
            for (int L = 0; L < NLK1; L++)
            {
                int L1 = L + 1;
                Complex CC = EMU2[L] - EMU2[L1];
                Complex C1 = CC * CU2;
                Complex C2 = C1 - DENS[L];
                Complex C3 = C1 + DENS[L1];
                Complex C4 = C1 - DENS[L] + DENS[L1];
                Complex C5 = C2 * C2;
                Complex C6 = C3 * C3;
                Complex C7 = C4 * C4 * CU2;
                float A1 = DENS[L] * DENS[L1];
                Complex C8 = WOA[L] * WOB[L];
                Complex C9 = WOA[L] * WOB[L1];
                Complex C10 = WOA[L1] * WOB[L];
                Complex C11 = WOA[L1] * WOB[L1];
                Complex C14 = A1 * C9;
                Complex C15 = A1 * C10;
                Complex C16 = CC * C1 * C8 * C11;
                Complex C17 = C5 * C11;
                Complex C18 = C6 * C8;
                Complex D1D = C7 + C17 + C15;
                Complex D2D = C16 + C18 + C14;
                Complex D1U = C7 + C18 + C14;
                Complex D2U = C16 + C17 + C15;
                Complex C19 = C3 * WOB[L] - C2 * WOB[L1];
                Complex C20 = C3 * WOA[L] - C2 * WOA[L1];
                Complex DD = D1D + D2D;
                RD[L1].a00 = (D2D - D1D) / DD;
                RU[L1].a00 = (D2U - D1U) / DD;
                Complex C21 = (CU + CU) * WOA[L];
                Complex C22 = (CU + CU) * WOB[L];
                Complex C23 = (CU + CU) * WOA[L1];
                Complex C24 = (CU + CU) * WOB[L1];
                Complex C25 = (C4 * C3 + CC * C2 * C11) / DD;
                RD[L1].a10 = -C21 * C25;
                Complex C35 = (C4 * C2 + CC * C3 * C8) / DD;
                RU[L1].a10 = C23 * C35;
                Complex C26 = DENS[L] / DD;
                TD[L1].a00 = (C26 + C26) * WOA[L] * C19;
                TD[L1].a10 = -C26 * C21 * (C4 + CC * C10);
                Complex C27 = (A1 + A1) * (C10 - C9);
                RD[L1].a11 = (D2D - D1D + C27) / DD;
                RD[L1].a01 = C22 * C25;
                TD[L1].a11 = (C26 + C26) * WOB[L] * C20;
                TD[L1].a01 = C26 * C22 * (C4 + CC * C9);
                Complex C36 = DENS[L1] / DD;
                TU[L1].a00 = (C36 + C36) * WOA[L1] * C19;
                TU[L1].a10 = -C36 * C23 * (C4 + CC * C9);
                RU[L1].a11 = (D2U - D1U - C27) / DD;
                RU[L1].a01 = -C24 * C35;
                TU[L1].a11 = (C36 + C36) * WOB[L1] * C20;
                TU[L1].a01 = C36 * C24 * (C4 + CC * C10);
            }

            // 初始化矩阵
            MT[NLK - 1] = Matrix2x2.Zero;
            MB[NLK1 - 1] = RD[NLK - 1];
            NB[0] = Matrix2x2.Zero;
            NT[1] = RU[1];
            G[0] = TU[1];

            // 计算EE矩阵
            for (int L = 1; L < NLK1; L++)
            {
                Complex C1 = CTH[L] * WZA[L];
                Complex C2 = CTH[L] * WZB[L];
                EE[L].a00 = Complex.Exp(C1);
                EE[L].a11 = Complex.Exp(C2);
                EE[L].a01 = Complex.Zero;
                EE[L].a10 = Complex.Zero;
            }

            // 反向循环计算
            for (int L = NLK1 - 1; L >= 1; L--)
            {
                int L1 = L - 1;
                Complex C1 = EE[L].a00;
                Complex C2 = EE[L].a11;
                Complex C3 = C1 * C2;

                MT[L].a00 = MB[L].a00 * C1 * C1;
                MT[L].a01 = MB[L].a01 * C3;
                MT[L].a10 = MB[L].a10 * C3;
                MT[L].a11 = MB[L].a11 * C2 * C2;

                E = Matrix2x2.Inverse(Matrix2x2.Identity - MT[L] * RU[L]);
                C = TU[L] * E;
                E = C * MT[L];
                MB[L1] = RD[L] + E * TD[L];
            }

            // 正向循环计算
            for (int L = 1; L < NLK1; L++)
            {
                int L1 = L + 1;
                Complex C1 = EE[L].a00;
                Complex C2 = EE[L].a11;
                Complex C3 = C1 * C2;

                NB[L].a00 = NT[L].a00 * C1 * C1;
                NB[L].a01 = NT[L].a01 * C3;
                NB[L].a10 = NT[L].a10 * C3;
                NB[L].a11 = NT[L].a11 * C2 * C2;

                C = NB[L] * RD[L1];
                E = Matrix2x2.Inverse(Matrix2x2.Identity - C);
                C = TD[L1] * E;
                E = C * NB[L];
                NT[L1] = RU[L1] + E * TU[L1];
            }

            // 计算QUU和QUD矩阵
            for (int L = 1; L < NLK1; L++)
            {
                C = MT[L] * NT[L];
                E = Matrix2x2.Identity - C;
                E = Matrix2x2.Inverse(E);

                C.a00 = EE[L].a00 * MB[L].a00;
                C.a01 = EE[L].a00 * MB[L].a01;
                C.a10 = EE[L].a11 * MB[L].a10;
                C.a11 = EE[L].a11 * MB[L].a11;

                QUU[L] = E;
                QUD[L] = E * C;
            }

            // 初始化边界条件
            QUU[0] = Matrix2x2.Zero;
            QUU[NLK - 1] = Matrix2x2.Identity;
            QUD[0] = MB[0];
            QUD[NLK - 1] = Matrix2x2.Zero;

            // 计算G矩阵
            for (int L = 0; L < NLK1; L++)
            {
                int L1 = L + 1;
                C = -RD[L1] * NB[L] + Matrix2x2.Identity;
                C = Matrix2x2.Inverse(C);
                G[L] = C * TU[L1];
            }

            // 计算TUP矩阵
            for (int IZ = 0; IZ < NZK; IZ++)
            {
                int L = LAY[IZ];
                int L1 = L - 1;

                TUP[L] = Matrix2x2.Identity;

                for (int LL = 0; LL <= L1; LL++)
                {
                    if (LL != 0)
                    {
                        TUP[L].a00 *= EE[LL].a00;
                        TUP[L].a01 *= EE[LL].a11;
                        TUP[L].a10 *= EE[LL].a00;
                        TUP[L].a11 *= EE[LL].a11;
                    }

                    TUP[L] = TUP[L] * G[LL];
                }
            }

            // 计算格林函数
            for (int IZ = 0; IZ < NZK; IZ++)
            {
                int L = LAY[IZ];
                Complex C1 = CM12 * AMPSV[IZ] * AIK2;
                SD[0, 0] = -C1 * AK / WZA[L];
                SU[0, 0] = SD[0, 0];
                SD[1, 0] = C1;
                SU[1, 0] = -SD[1, 0];

                Complex C2 = AMPSV[IZ] * (AK2 + AK2);
                Complex C3 = AMPSV[IZ] * AK * (AK2 / WZB[L] - WZB[L]);
                SD[0, 1] = CM13 * C2;
                SU[0, 1] = -SD[0, 1];
                SD[1, 1] = CM13 * C3;
                SU[1, 1] = SD[1, 1];

                SD[0, 2] = CM23 * C2;
                SU[0, 2] = -SD[0, 2];
                SD[1, 2] = CM23 * C3;
                SU[1, 2] = SD[1, 2];

                Complex C4 = AMPSV[IZ] * AIK2 / 2.0 * (CM11 - CM22);
                SD[0, 3] = -C4 * AK / WZA[L];
                SU[0, 3] = SD[0, 3];
                SD[1, 3] = C4;
                SU[1, 3] = -SD[1, 3];

                SD[0, 4] = AMPSV[IZ] * AIK * (AK2 / WZA[L] * (CM11 + CM22) / 2.0 + WZA[L] * CM33);
                SU[0, 4] = SD[0, 4];
                SD[1, 4] = AMPSV[IZ] * AIK2 * (CM33 - (CM11 + CM22) / 2.0);
                SU[1, 4] = -SD[1, 4];

                // 计算位移
                for (int IM = 0; IM < 5; IM++)
                {
                    for (int IP = 0; IP < 2; IP++)
                    {
                        BS[IP] = Complex.Zero;
                        for (int JP = 0; JP < 2; JP++)
                        {
                            BS[IP] += QUU[L][IP, JP] * SU[JP, IM] + QUD[L][IP, JP] * SD[JP, IM] * EE[L][JP, JP];
                        }
                    }

                    for (int IP = 0; IP < 2; IP++)
                    {
                        BL[IP] = Complex.Zero;
                        for (int JP = 0; JP < 2; JP++)
                        {
                            BL[IP] += TUP[L][IP, JP] * BS[JP];
                        }
                    }

                    DU1[IZ, IM] = BL[0] + WZB[0] / AK * BL[1];
                    DU2[IZ, IM] = Complex.ImaginaryOne * (WZA[0] * BL[0] - AK * BL[1]);
                }
            }

            // 计算SH波反射和透射矩阵
            for (int L = 0; L < NLK1; L++)
            {
                int L1 = L + 1;
                Complex C1 = EMU[L] * WOB[L];
                Complex C2 = EMU[L1] * WOB[L1];
                RDSH[L1] = (C1 - C2) / (C1 + C2);
                TDSH[L1] = (C1 + C1) / (C1 + C2);
                RUSH[L1] = -RDSH[L1];
                TUSH[L1] = (C2 + C2) / (C1 + C2);
            }

            // 初始化SH波反射和透射矩阵
            MTSH[NLK - 1] = Complex.Zero;
            MBSH[NLK1 - 1] = RDSH[NLK - 1];
            NBSH[0] = Complex.Zero;
            NTSH[1] = RUSH[1];
            GSH[0] = TUSH[1];

            // 计算EESH矩阵
            for (int L = 1; L < NLK1; L++)
            {
                Complex C2 = CTH[L] * WZB[L];
                EESH[L] = Complex.Exp(C2);
            }

            // 反向循环计算SH波矩阵
            for (int L = NLK1 - 1; L >= 1; L--)
            {
                int L1 = L - 1;
                MTSH[L] = MBSH[L] * EESH[L] * EESH[L];
                MBSH[L1] = RDSH[L] + TDSH[L] * TUSH[L] * MTSH[L] / (Complex.One - RUSH[L] * MTSH[L]);
            }

            // 正向循环计算SH波矩阵
            for (int L = 1; L < NLK1; L++)
            {
                int L1 = L + 1;
                NBSH[L] = NTSH[L] * EESH[L] * EESH[L];
                NTSH[L1] = RUSH[L1] + TDSH[L1] * TUSH[L1] * NBSH[L] / (Complex.One - RDSH[L1] * NBSH[L]);
            }

            // 计算QUU和QUD矩阵（SH波）
            for (int L = 1; L < NLK1; L++)
            {
                QUUSH[L] = Complex.One / (Complex.One - MTSH[L] * NTSH[L]);
                QUDSH[L] = EESH[L] * MBSH[L] / (Complex.One - MTSH[L] * NTSH[L]);
            }

            QUUSH[0] = Complex.Zero;
            QUDSH[0] = MBSH[0];
            QUDSH[NLK - 1] = Complex.Zero;
            QUUSH[NLK - 1] = Complex.One;

            // 计算G矩阵（SH波）
            for (int L = 1; L < NLK1; L++)
            {
                int L1 = L + 1;
                GSH[L] = TUSH[L1] / (Complex.One - RDSH[L1] * NBSH[L]);
            }

            // 计算TUPSH矩阵
            for (int IZ = 0; IZ < NZK; IZ++)
            {
                int L = LAY[IZ];
                int L1 = L - 1;
                TUPSH[L] = Complex.One;

                for (int LL = 0; LL <= L1; LL++)
                {
                    if (LL != 0) TUPSH[L] = TUPSH[L] * EESH[LL];
                    TUPSH[L] = TUPSH[L] * GSH[LL];
                }
            }

            // 计算SH波格林函数
            for (int IZ = 0; IZ < NZK; IZ++)
            {
                int L = LAY[IZ];
                SD[2, 0] = CM12 * AMSH[IZ] * AIK / WZB[L];
                SU[2, 0] = SD[2, 0];
                SD[2, 1] = CM13 * AMSH[IZ];
                SU[2, 1] = -SD[2, 1];
                SD[2, 2] = -CM23 * AMSH[IZ];
                SU[2, 2] = -SD[2, 2];
                SD[2, 3] = AMSH[IZ] * AIK / (2.0 * WZB[L]) * (CM22 - CM11);
                SU[2, 3] = SD[2, 3];

                for (int IM = 0; IM < 4; IM++)
                {
                    Complex BSSH = QUUSH[L] * SU[2, IM] + QUDSH[L] * SD[2, IM] * EESH[L];
                    Complex BLSH = TUPSH[L] * BSSH;
                    DU3[IZ, IM] = BLSH;
                }
            }

            // 计算位移场
            for (int IZ = 0; IZ < NZK; IZ++)
            {
                for (int IX = 0; IX < NX; IX++)
                {
                    for (int IR = 0; IR < NR; IR++)
                    {
                        float AJ1R = AJ1[IX, IZ, IR, K] / R[IX, IZ, IR];
                        float AJKR = AK * AJ0[IX, IZ, IR, K] - AJ1R;
                        float AJ2 = (AJ1R + AJ1R) / AK - AJ0[IX, IZ, IR, K];
                        float AJ2R = (AJ2 + AJ2) / R[IX, IZ, IR];
                        float AJ1K = AK * AJ1[IX, IZ, IR, K];
                        float DAJ2 = AJ1K - AJ2R;

                        EJ1[IX, IR, IZ] +=
                            S2T[IX, IZ, IR] * (DAJ2 * DU1[IZ, 0] - AJ2R * DU3[IZ, 0]) +
                            CT[IX, IZ, IR] * (AJKR * DU1[IZ, 1] + AJ1R * DU3[IZ, 1]) +
                            ST[IX, IZ, IR] * (AJKR * DU1[IZ, 2] - AJ1R * DU3[IZ, 2]) +
                            C2T[IX, IZ, IR] * (DAJ2 * DU1[IZ, 3] + AJ2R * DU3[IZ, 3]) -
                            AJ1K * DU1[IZ, 4];

                        EJ2[IX, IR, IZ] +=
                            C2T[IX, IZ, IR] * (AJ2R * DU1[IZ, 0] - DAJ2 * DU3[IZ, 0]) -
                            ST[IX, IZ, IR] * (AJ1R * DU1[IZ, 1] + AJKR * DU3[IZ, 1]) +
                            CT[IX, IZ, IR] * (AJ1R * DU1[IZ, 2] - AJKR * DU3[IZ, 2]) -
                            S2T[IX, IZ, IR] * (AJ2R * DU1[IZ, 3] + DAJ2 * DU3[IZ, 3]);

                        EJ3[IX, IR, IZ] +=
                            AJ2 * (S2T[IX, IZ, IR] * DU2[IZ, 0] + C2T[IX, IZ, IR] * DU2[IZ, 3]) +
                            AJ1[IX, IZ, IR, K] * (CT[IX, IZ, IR] * DU2[IZ, 1] + ST[IX, IZ, IR] * DU2[IZ, 2]) +
                            AJ0[IX, IZ, IR, K] * DU2[IZ, 4];
                    }
                }
            }

            // 检查波数级数收敛性
            bool converged = true;
            for (int IR = 0; IR < NR; IR++)
            {
                for (int IX = 0; IX < NX; IX += 10)
                {
                    int IZ = NZK - 1;
                    float A10 = (float)(EPS * Complex.Abs(EJ1[IX, IR, IZ]));
                    float A11 = (float)(EPS * Complex.Abs(EJ2[IX, IR, IZ]));
                    float A12 = (float)(EPS * Complex.Abs(EJ3[IX, IR, IZ]));

                    float AJ1R = AJ1[IX, IZ, IR, K] / R[IX, IZ, IR];
                    float AJKR = AK * AJ0[IX, IZ, IR, K] - AJ1R;
                    float AJ2 = (AJ1R + AJ1R) / AK - AJ0[IX, IZ, IR, K];
                    float AJ2R = (AJ2 + AJ2) / R[IX, IZ, IR];
                    float AJ1K = AK * AJ1[IX, IZ, IR, K];
                    float DAJ2 = AJ1K - AJ2R;

                    Complex C1 =
                        S2T[IX, IZ, IR] * (DAJ2 * DU1[IZ, 0] - AJ2R * DU3[IZ, 0]) +
                        CT[IX, IZ, IR] * (AJKR * DU1[IZ, 1] + AJ1R * DU3[IZ, 1]) +
                        ST[IX, IZ, IR] * (AJKR * DU1[IZ, 2] - AJ1R * DU3[IZ, 2]) +
                        C2T[IX, IZ, IR] * (DAJ2 * DU1[IZ, 3] + AJ2R * DU3[IZ, 3]) -
                        AJ1K * DU1[IZ, 4];

                    Complex C2 =
                        C2T[IX, IZ, IR] * (AJ2R * DU1[IZ, 0] - DAJ2 * DU3[IZ, 0]) -
                        ST[IX, IZ, IR] * (AJ1R * DU1[IZ, 1] + AJKR * DU3[IZ, 1]) +
                        CT[IX, IZ, IR] * (AJ1R * DU1[IZ, 2] - AJKR * DU3[IZ, 2]) -
                        S2T[IX, IZ, IR] * (AJ2R * DU1[IZ, 3] + DAJ2 * DU3[IZ, 3]);

                    Complex C3 =
                        AJ2 * (S2T[IX, IZ, IR] * DU2[IZ, 0] + C2T[IX, IZ, IR] * DU2[IZ, 3]) +
                        AJ1[IX, IZ, IR, K] * (CT[IX, IZ, IR] * DU2[IZ, 1] + ST[IX, IZ, IR] * DU2[IZ, 2]) +
                        AJ0[IX, IZ, IR, K] * DU2[IZ, 4];

                    float A14 = (float)Complex.Abs(C1);
                    float A15 = (float)Complex.Abs(C2);
                    float A16 = (float)Complex.Abs(C3);

                    if (A14 > A10 || A15 > A11 || A16 > A12)
                    {
                        converged = false;
                        break;
                    }
                }
                if (!converged) break;
            }

            if (!converged) continue;

            if (NZK == 1) break;
            NLK = LAY[NZK - 1] - 1;
            NLK1 = NLK - 1;
            NZK--;
        }

        // Console.WriteLine($"{IF} {K}");

        // 计算位移
        for (int IR = 0; IR < NR; IR++)
        {
            for (int J = 0; J < 3; J++)
            {
                U[IF, IR, J] = Complex.Zero;
            }

            for (int IX = 0; IX < NX; IX++)
            {
                for (int IZ = 0; IZ < NZ; IZ++)
                {
                    Complex EVV = EV[IX, IZ] * Complex.Pow(TEV[IX, IZ], IF);
                    Complex C1 = EJ1[IX, IR, IZ] * EVV;
                    Complex C2 = EJ2[IX, IR, IZ] * EVV;
                    Complex C3 = C1 * CT[IX, IZ, IR] - C2 * ST[IX, IZ, IR];
                    Complex C4 = C2 * CT[IX, IZ, IR] + C1 * ST[IX, IZ, IR];
                    Complex C7 = EJ3[IX, IR, IZ] * EVV;

                    if (RT)
                    {
                        C3 = C1;
                        C4 = C2;
                    }

                    U[IF, IR, 0] += C3;
                    U[IF, IR, 1] += C4;
                    U[IF, IR, 2] += C7;
                }
            }
        }
    }

    private void CalculateAllFrequency(bool multithread = false)
    {
        if (multithread)
        {
            Parallel.For(0, NFREQ, IF =>
            {
                CalculateFrequency(IF);
            });
        }
        else
        {
            for (int IF = 0; IF < NFREQ; IF++)
            {
                CalculateFrequency(IF);
            }
        }
    }

    // 计算时域解
    float[,,,] sy; // cm, cm/s, cm/s^2
    private void CalculateTimeDomain()
    {
        float[] yyy = new float[NTMAX];
        float[] y = new float[NTMAX + NTMAX + 3];
        sy = new float[NTMAX, NRMAX, 3, 3];
        int n3 = NTIME + NTIME + 3;
        float tex1 = -AW * DT;
        tex1 = (float)Math.Exp(tex1);
        float ex7 = 1.0f;
        for (int i = 0; i < NTIME; i++)
        {
            yyy[i] = ex7;
            ex7 *= tex1;
        }

        for (int is_ = 0; is_ < 3; is_++)
        {
            for (int ir = 0; ir < NR; ir++)
            {
                for (int j = 0; j < 3; j++)
                {
                    for (int i = 0; i < n3; i++) y[i] = 0.0f;

                    for (int i = 0; i < NFREQ; i++)
                    {
                        Complex c1 = U[i, ir, j] * SOURCE[i, is_];
                        int i1 = i * 2;
                        int i2 = i * 2 + 1;
                        y[i1] = (float)c1.Real;
                        y[i2] = (float)c1.Imaginary;
                        y[n3 - i1 - 2] = -y[i2];
                        y[n3 - i2 - 2] = y[i1];
                    }

                    Four1(y, NTIME, 1);

                    for (int i = 0; i < NTIME; i++)
                    {
                        sy[i, ir, j, is_] = y[i * 2] * yyy[i] - y[0];
                    }
                }
            }
        }
    }

    private void WriteOutputData()
    {
        // 输出结果
        // using StreamWriter outputFile = new StreamWriter($"{Application.streamingAssetsPath}/Python/seismic_data/MOD_output");
        // for (int is_ = 0; is_ < 2; is_++)
        //     for (int ir = 0; ir < NR; ir++)
        //         for (int j = 0; j < 3; j++)
        //         {
        //             float T = 0.0f;
        //             for (int i = 0; i < NTIME; i++)
        //             {
        //                 outputFile.WriteLine($"{is_ + 1} {ir + 1} {j + 1} {T} {sy[i, ir, j, is_]}");
        //                 T += DT;
        //             }
        //         }

        Seismic.SeismicData data = new Seismic.SeismicData()
        {
            nt = NTIME,
            dt = DT,
        };
        for (int ir = 0; ir < NR; ir++)
        {
            data.north = new List<float>();
            data.east = new List<float>();
            string fileName = $"Layer_Acc_Receiver_{ir + 1}.json";
            for (int i = 0; i < NTIME; i++)
            {
                // cm/s^2 -> m/s^2
                data.north.Add(sy[i, ir, 0, 2] / 100);
                data.east.Add(sy[i, ir, 1, 2] / 100);
            }
            data.ToJson(fileName);
        }
    }

    private void SendDataToGPU()
    {
        computeShader.SetInt("NL", NL);
        computeShader.SetInt("NR", NR);
        computeShader.SetInt("NX", NX);
        computeShader.SetInt("NZ", NZ);
        computeShader.SetInt("M", M);
        computeShader.SetInt("NTIME", NTIME);
        computeShader.SetInt("NFREQ", NFREQ);

        computeShader.SetFloat("DFREQ", DFREQ);
        computeShader.SetFloat("AW", AW);
        computeShader.SetFloat("XL", XL);
        computeShader.SetFloat("PIL", PIL);
        computeShader.SetFloat("TSOURCE", TSOURCE);
        computeShader.SetFloat("CM11", CM11);
        computeShader.SetFloat("CM12", CM12);
        computeShader.SetFloat("CM13", CM13);
        computeShader.SetFloat("CM22", CM22);
        computeShader.SetFloat("CM23", CM23);
        computeShader.SetFloat("CM33", CM33);
        computeShader.SetFloat("SDI", SDI);
        computeShader.SetFloat("DL", DL);
        computeShader.SetFloat("DZ", DZ);
        computeShader.SetInt("RT", RT ? 1 : 0);

        const int c1 = NLMX;
        const int c2 = NXMAX * NZMAX;
        const int c3 = NXMAX * NZMAX * NRMAX;
        const int c4 = NXMAX * NZMAX * NRMAX * MMAX;
        int sfloat = sizeof(float);
        int scomplex = sizeof(double) * 2;
        int smatrix = scomplex * 4;

        // 1D 数组
        AL0Buffer = new ComputeBuffer(c1, sfloat);
        AL0Buffer.SetData(AL0);
        BE0Buffer = new ComputeBuffer(c1, sfloat);
        BE0Buffer.SetData(BE0);
        DENSBuffer = new ComputeBuffer(c1, sfloat);
        DENSBuffer.SetData(DENS);
        QPBuffer = new ComputeBuffer(c1, sfloat);
        QPBuffer.SetData(QP);
        QSBuffer = new ComputeBuffer(c1, sfloat);
        QSBuffer.SetData(QS);

        AQPBuffer = new ComputeBuffer(c1, scomplex);
        AQPBuffer.SetData(AQP);

        AQSBuffer = new ComputeBuffer(c1, scomplex);
        AQSBuffer.SetData(AQS);

        CTHBuffer = new ComputeBuffer(c1, scomplex);
        CTHBuffer.SetData(CTH);

        LAYBuffer = new ComputeBuffer(NZMAX, sizeof(int));
        LAYBuffer.SetData(LAY);

        // 2D 数组
        var ComplexArr = new Complex[NXMAX * NZMAX];

        for (int i = 0; i < NXMAX; i++)
        {
            for (int j = 0; j < NZMAX; j++)
            {
                int index = i * NZMAX + j;
                ComplexArr[index] = EV[i, j];
            }
        }
        EVBuffer = new ComputeBuffer(c2, scomplex);
        EVBuffer.SetData(ComplexArr);

        for (int i = 0; i < NXMAX; i++)
        {
            for (int j = 0; j < NZMAX; j++)
            {
                int index = i * NZMAX + j;
                ComplexArr[index] = TEV[i, j];
            }
        }
        TEVBuffer = new ComputeBuffer(c2, scomplex);
        TEVBuffer.SetData(ComplexArr);

        // 3D 数组
        float[] floatArr = new float[NXMAX * NZMAX * NRMAX];

        for (int i = 0; i < NXMAX; i++)
            for (int j = 0; j < NZMAX; j++)
                for (int k = 0; k < NRMAX; k++)
                    floatArr[i * NZMAX * NRMAX + j * NRMAX + k] = R[i, j, k];
        RBuffer = new ComputeBuffer(c3, sfloat);
        RBuffer.SetData(floatArr);

        for (int i = 0; i < NXMAX; i++)
            for (int j = 0; j < NZMAX; j++)
                for (int k = 0; k < NRMAX; k++)
                    floatArr[i * NZMAX * NRMAX + j * NRMAX + k] = ST[i, j, k];
        STBuffer = new ComputeBuffer(c3, sfloat);
        STBuffer.SetData(floatArr);

        for (int i = 0; i < NXMAX; i++)
            for (int j = 0; j < NZMAX; j++)
                for (int k = 0; k < NRMAX; k++)
                    floatArr[i * NZMAX * NRMAX + j * NRMAX + k] = CT[i, j, k];
        CTBuffer = new ComputeBuffer(c3, sfloat);
        CTBuffer.SetData(floatArr);

        for (int i = 0; i < NXMAX; i++)
            for (int j = 0; j < NZMAX; j++)
                for (int k = 0; k < NRMAX; k++)
                    floatArr[i * NZMAX * NRMAX + j * NRMAX + k] = S2T[i, j, k];
        S2TBuffer = new ComputeBuffer(c3, sfloat);
        S2TBuffer.SetData(floatArr);

        for (int i = 0; i < NXMAX; i++)
            for (int j = 0; j < NZMAX; j++)
                for (int k = 0; k < NRMAX; k++)
                    floatArr[i * NZMAX * NRMAX + j * NRMAX + k] = C2T[i, j, k];
        C2TBuffer = new ComputeBuffer(c3, sfloat);
        C2TBuffer.SetData(floatArr);

        // 4D 数组
        floatArr = new float[NXMAX * NZMAX * NRMAX * MMAX];
        for (int i = 0; i < NXMAX; i++)
            for (int j = 0; j < NZMAX; j++)
                for (int k = 0; k < NRMAX; k++)
                    for (int l = 0; l < MMAX; l++)
                        floatArr[i * NZMAX * NRMAX * MMAX + j * NRMAX * MMAX + k * MMAX + l] = AJ0[i, j, k, l];
        AJ0Buffer = new ComputeBuffer(c4, sfloat);
        AJ0Buffer.SetData(floatArr);

        for (int i = 0; i < NXMAX; i++)
            for (int j = 0; j < NZMAX; j++)
                for (int k = 0; k < NRMAX; k++)
                    for (int l = 0; l < MMAX; l++)
                        floatArr[i * NZMAX * NRMAX * MMAX + j * NRMAX * MMAX + k * MMAX + l] = AJ1[i, j, k, l];
        AJ1Buffer = new ComputeBuffer(c4, sfloat);
        AJ1Buffer.SetData(floatArr);

        UBuffer = new ComputeBuffer(NFREQ * NRMAX * 3, scomplex);

        SOURCEBuffer = new ComputeBuffer(NFREQ * 3, scomplex);

        MatrixesBuffer = new ComputeBuffer(13 * NFREQ * NLMX, smatrix);

        int cbcount = 23 * NFREQ * NLMX
            + 3 * NFREQ * NX * NR * NZ
            + 3 * NFREQ * NZMAX * 5
            + 2 * NFREQ * NZMAX
            + 2 * NFREQ * 3 * 5;
        ComplexesBuffer = new ComputeBuffer(cbcount, scomplex);

        computeShader.SetBuffer(kernelHandle, "AL0", AL0Buffer);
        computeShader.SetBuffer(kernelHandle, "BE0", BE0Buffer);
        computeShader.SetBuffer(kernelHandle, "DENS", DENSBuffer);
        computeShader.SetBuffer(kernelHandle, "QP", QPBuffer);
        computeShader.SetBuffer(kernelHandle, "QS", QSBuffer);
        computeShader.SetBuffer(kernelHandle, "AQP", AQPBuffer);
        computeShader.SetBuffer(kernelHandle, "AQS", AQSBuffer);
        computeShader.SetBuffer(kernelHandle, "CTH", CTHBuffer);
        computeShader.SetBuffer(kernelHandle, "LAY", LAYBuffer);
        computeShader.SetBuffer(kernelHandle, "EV", EVBuffer);
        computeShader.SetBuffer(kernelHandle, "TEV", TEVBuffer);
        computeShader.SetBuffer(kernelHandle, "R", RBuffer);
        computeShader.SetBuffer(kernelHandle, "ST", STBuffer);
        computeShader.SetBuffer(kernelHandle, "CT", CTBuffer);
        computeShader.SetBuffer(kernelHandle, "S2T", S2TBuffer);
        computeShader.SetBuffer(kernelHandle, "C2T", C2TBuffer);
        computeShader.SetBuffer(kernelHandle, "AJ0", AJ0Buffer);
        computeShader.SetBuffer(kernelHandle, "AJ1", AJ1Buffer);
        computeShader.SetBuffer(kernelHandle, "U", UBuffer);
        computeShader.SetBuffer(kernelHandle, "SOURCE", SOURCEBuffer);
        computeShader.SetBuffer(kernelHandle, "Matrixes", MatrixesBuffer);
        computeShader.SetBuffer(kernelHandle, "Complexes", ComplexesBuffer);
    }

    private void ReceiveDataFromGPU()
    {
        Complex[] data = new Complex[NFREQ * NRMAX * 3];
        UBuffer.GetData(data);
        // Debug.Log($"U[0] - {data[0].Real} {data[0].Imaginary}");
        // Debug.Log($"U[1] - {data[1].Real} {data[1].Imaginary}");
        // Debug.Log($"U[2] - {data[2].Real} {data[2].Imaginary}");
        // Debug.Log($"U[3] - {data[3].Real} {data[3].Imaginary}");
        // Debug.Log($"U[4] - {data[4].Real} {data[4].Imaginary}");
        // Debug.Log($"U[5] - {data[5].Real} {data[5].Imaginary}");
        // Debug.Log($"U[6] - {data[6].Real} {data[6].Imaginary}");
        // Debug.Log($"U[7] - {data[7].Real} {data[7].Imaginary}");
        // Debug.Log($"U[8] - {data[8].Real} {data[8].Imaginary}");
        // Debug.Log($"U[9] - {data[9].Real} {data[9].Imaginary}");

        for (int i = 0; i < NFREQ; i++)
            for (int j = 0; j < NRMAX; j++)
                for (int k = 0; k < 3; k++)
                {
                    int index = i * NRMAX * 3 + j * 3 + k;
                    U[i, j, k] = data[index];
                }

        data = new Complex[NFREQ * 3];
        SOURCEBuffer.GetData(data);

        for (int i = 0; i < NFREQ; i++)
            for (int j = 0; j < 3; j++)
            {
                int index = i * 3 + j;
                SOURCE[i, j] = data[index];
            }

        AL0Buffer.Release();
        BE0Buffer.Release();
        DENSBuffer.Release();
        QPBuffer.Release();
        QSBuffer.Release();
        AQPBuffer.Release();
        AQSBuffer.Release();
        CTHBuffer.Release();
        LAYBuffer.Release();
        EVBuffer.Release();
        TEVBuffer.Release();
        RBuffer.Release();
        STBuffer.Release();
        CTBuffer.Release();
        S2TBuffer.Release();
        C2TBuffer.Release();
        AJ0Buffer.Release();
        AJ1Buffer.Release();

        UBuffer.Release();
        SOURCEBuffer.Release();
        MatrixesBuffer.Release();
        ComplexesBuffer.Release();
    }

    private const int numThreadsX = 128;
    private void CalculateAllFrequencyOnGPU()
    {
        // Debug.Log($"GPU Name: {SystemInfo.graphicsDeviceName}");
        // Debug.Log($"GPU Memory: {SystemInfo.graphicsMemorySize}MB");

        SendDataToGPU();

        computeShader.Dispatch(kernelHandle, (NFREQ + numThreadsX - 1) / numThreadsX, 1, 1);

        ReceiveDataFromGPU();
    }

    public void Run()
    {
        InitializeComputeShader();

        // C# (Fortran translation): nms
        // C# (+Matrix optimization): 262250ms
        // C# (+Multi-thread): 25543ms
        // Compute Shader: 1528ms

        Stopwatch stopwatch = new Stopwatch();
        stopwatch.Start();

        CalculateParameters();
        CalculateAllFrequencyOnGPU();
        CalculateTimeDomain();
        WriteOutputData();
        Debug.Log($"LSP Run: {stopwatch.ElapsedMilliseconds}ms");
    }

    public IEnumerator RunGPUCoroutine()
    {
        Stopwatch stopwatch = new Stopwatch();
        stopwatch.Start();
        InitializeComputeShader();
        yield return null;
        CalculateParameters();
        yield return null;
        // Debug.Log($"LSP Run: {stopwatch.ElapsedMilliseconds}ms");
        SendDataToGPU();
        yield return null;
        // Debug.Log($"LSP Send: {stopwatch.ElapsedMilliseconds}ms");

        computeShader.Dispatch(kernelHandle, (NFREQ + numThreadsX - 1) / numThreadsX, 1, 1);
        AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(UBuffer);
        while (!request.done)
        {
            yield return 10;
        }

        // for (int IF_Offset = 0; IF_Offset < NFREQ; IF_Offset += numThreadsX)
        // {
        //     computeShader.SetInt("IF_Offset", IF_Offset);
        //     computeShader.Dispatch(kernelHandle, 1, 1, 1);
        //     yield return null;
        // }

        // Debug.Log($"LSP Dispatch: {stopwatch.ElapsedMilliseconds}ms");
        ReceiveDataFromGPU();
        yield return null;
        CalculateTimeDomain();
        yield return null;
        WriteOutputData();
        Debug.Log($"LSP Run: {stopwatch.ElapsedMilliseconds}ms");
    }

    // 辅助方法
    private void Inv2(Complex[,] a)
    {
        Complex[,] b = new Complex[2, 2];
        for (int i = 0; i < 2; i++)
            for (int j = 0; j < 2; j++)
                b[i, j] = a[i, j];

        Complex det = b[0, 0] * b[1, 1] - b[0, 1] * b[1, 0];
        a[0, 0] = b[1, 1] / det;
        a[1, 1] = b[0, 0] / det;
        a[0, 1] = -b[0, 1] / det;
        a[1, 0] = -b[1, 0] / det;
    }

    private void Four1(float[] data, int n, int isign)
    {
        int nn = n; // Number of complex points

        // --- Bit-reversal permutation ---
        int j = 0;
        for (int i = 0; i < n; i++)
        {
            int i_idx = i * 2;      // Index of real part for complex point i
            int j_idx = j * 2;      // Index of real part for complex point j

            if (j > i)
            {
                // Swap real parts
                float tempr = data[j_idx];
                data[j_idx] = data[i_idx];
                data[i_idx] = tempr;

                // Swap imaginary parts
                float tempi = data[j_idx + 1];
                data[j_idx + 1] = data[i_idx + 1];
                data[i_idx + 1] = tempi;
            }

            int m = nn / 2; // Equivalent to Fortran's ip1 = ip3 / 2 logic start
            while (m >= 1 && j >= m)
            {
                j -= m;
                m /= 2;
            }
            j += m;
        }

        // --- FFT Butterfly calculation ---
        int mmax = 1;
        while (nn > mmax)
        {
            int istep = mmax * 2;
            float theta = (float)(Math.PI / (isign * mmax));
            float wtemp = (float)Math.Sin(0.5 * theta);
            float wpr = (float)(-2.0 * wtemp * wtemp); // Real part of W_step - 1 = cos(theta) - 1
            float wpi = (float)Math.Sin(theta);      // Imaginary part of W_step = sin(theta)
            float wr = 1.0f;                   // Real part of current W = 1.0
            float wi = 0.0f;                   // Imaginary part of current W = 0.0

            for (int m = 0; m < mmax; m++)
            {
                for (int i = m; i < nn; i += istep)
                {
                    int i_idx = i * 2;          // Real index for i
                    j = i + mmax;               // Index of the other element in the butterfly
                    int j_idx = j * 2;          // Real index for j

                    // temp = W * data[j]
                    float tempr = wr * data[j_idx] - wi * data[j_idx + 1];
                    float tempi = wr * data[j_idx + 1] + wi * data[j_idx];

                    // data[j] = data[i] - temp
                    data[j_idx] = data[i_idx] - tempr;
                    data[j_idx + 1] = data[i_idx + 1] - tempi;

                    // data[i] = data[i] + temp
                    data[i_idx] = data[i_idx] + tempr;
                    data[i_idx + 1] = data[i_idx + 1] + tempi;
                }
                // Update W = W * W_step
                wtemp = wr;
                wr = wr * wpr - wi * wpi + wr;
                wi = wi * wpr + wtemp * wpi + wi;
            }
            mmax = istep; // Double the group size for the next stage
        }
    }
}
