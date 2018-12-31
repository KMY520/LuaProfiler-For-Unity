﻿/*
* ==============================================================================
* Filename: NetWorkServer
* Created:  2018/7/13 14:29:22
* Author:   エル・プサイ・コングリィ
* Purpose:  
* ==============================================================================
*/

namespace MikuLuaProfiler
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading;
    using UnityEditor;

    [InitializeOnLoad]
    public static class StartUp
    {
        static bool isPlaying = false;

        static StartUp()
        {
#if UNITY_2017_1_OR_NEWER
            EditorApplication.playModeStateChanged += (state) =>
            {
                if (isPlaying == true && EditorApplication.isPlaying == false)
                {
                    NetWorkServer.Close();
                }

                isPlaying = EditorApplication.isPlaying;
            };
#else
            EditorApplication.playmodeStateChanged += () =>
            {
                if (isPlaying == true && EditorApplication.isPlaying == false)
                {
                    NetWorkServer.Close();
                }

                isPlaying = EditorApplication.isPlaying;
            };
#endif
        }
    }

    public static class NetWorkServer
    {
        private static TcpListener tcpLister;
        private static TcpClient tcpClient = null;
        private static Thread acceptThread;

        private const int PACK_HEAD = 0x23333333;
        private static Action<Sample> m_onReceiveSample;
        private static Action<LuaRefInfo> m_onReceiveRef;

        public static bool CheckIsReceiving()
        {
            return tcpClient != null;
        }

        public static void RegisterOnReceiveSample(Action<Sample> onReceive)
        {
            m_onReceiveSample = onReceive;
        }

        public static void RegisterOnReceiveRefInfo(Action<LuaRefInfo> onReceive)
        {
            m_onReceiveRef = onReceive;
        }

        public static void BeginListen(string ip, int port)
        {
            if (tcpLister != null) return;
            m_strCacheDict.Clear();
            IPAddress myIP = IPAddress.Parse(ip);
            tcpLister = new TcpListener(myIP, port);
            tcpLister.Start();
            // 启动一个线程来接受请求
            acceptThread = new Thread(acceptClientConnect);
            acceptThread.Start();
        }

        // 接受请求
        private static void acceptClientConnect()
        {
            UnityEngine.Debug.Log("begin to listener");
            try
            {
                tcpClient = tcpLister.AcceptTcpClient();
                UnityEngine.Debug.Log("link start");
                tcpClient.ReceiveTimeout = 1000000;
            }
            catch
            {
                UnityEngine.Debug.Log("stop listener");
                Thread.Sleep(1000);
            }
            NetworkStream ns = tcpClient.GetStream();
            BinaryReader br = new BinaryReader(ns);
            ns.ReadTimeout = 600000;
            //sign为true 循环接受数据
            while (true)
            {
                try
                {
                    if (tcpClient == null)
                    {
                        Close();
                        return;
                    }

                    if (ns.CanRead && ns.DataAvailable)
                    {
                        try
                        {
                            //处理粘包
                            while (br.ReadInt32() == PACK_HEAD)
                            {
                                int messageId = br.ReadInt32();
                                switch (messageId)
                                {
                                    case 0:
                                        {
                                            Sample s = Deserialize(br);
                                            if (m_onReceiveSample != null)
                                            {
                                                m_onReceiveSample(s);
                                            }
                                        }
                                        break;
                                    case 1:
                                        {
                                            var r = DeserializeRef(br);
                                            if (m_onReceiveRef != null)
                                            {
                                                m_onReceiveRef(r);
                                            }
                                        }
                                        break;
                                }

                            }
                        }
#pragma warning disable 0168
                        catch (EndOfStreamException ex)
                        {
                            Close();
                            return;
                        }
#pragma warning restore 0168
                    }
                }
#pragma warning disable 0168
                catch (Exception e)
                {
                    UnityEngine.Debug.Log(e);
                    Close();
                }
#pragma warning restore 0168
                Thread.Sleep(10);
            }
        }

        public static void Close()
        {
            try
            {
                if (tcpLister != null)
                {
                    UnityEngine.Debug.Log("stop");
                    tcpLister.Stop();
                    tcpLister = null;
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.Log(e);
            }
            if (acceptThread != null)
            {
                try
                {
                    acceptThread.Abort();
                }
                catch { }
                acceptThread = null;
            }
        }

        private static Dictionary<int, string> m_strCacheDict = new Dictionary<int, string>(4096);
        public static Sample Deserialize(BinaryReader br)
        {
            Sample s = Sample.Create();

            s.calls = br.ReadInt32();
            s.frameCount = br.ReadInt32();
            s.fps = br.ReadSingle();
            s.pss = br.ReadInt32();
            s.power = br.ReadSingle();
            s.costLuaGC = br.ReadInt32();
            s.costMonoGC = br.ReadInt32();

            bool isRef = br.ReadBoolean();
            int index = br.ReadInt32();
            if (!isRef)
            {
                int len = br.ReadInt32();
                byte[] datas = br.ReadBytes(len);
                s.name = string.Intern(Encoding.UTF8.GetString(datas));
                m_strCacheDict[index] = s.name;
            }
            else
            {
                s.name = m_strCacheDict[index];
            }

            s.costTime = br.ReadInt32();
            s.currentLuaMemory = br.ReadInt32();
            s.currentMonoMemory = br.ReadInt32();
            int count = br.ReadUInt16();
            for (int i = 0, imax = count; i < imax; i++)
            {
                Deserialize(br).fahter = s;
            }
            return s;
        }

        public static LuaRefInfo DeserializeRef(BinaryReader br)
        {
            LuaRefInfo refInfo = LuaRefInfo.Create();
            refInfo.cmd = br.ReadByte();

            bool isRef = br.ReadBoolean();
            int index = br.ReadInt32();
            if (!isRef)
            {
                int len = br.ReadInt32();
                byte[] datas = br.ReadBytes(len);
                refInfo.name = string.Intern(Encoding.UTF8.GetString(datas));
                m_strCacheDict[index] = refInfo.name;
            }
            else
            {
                refInfo.name = m_strCacheDict[index];
            }

            isRef = br.ReadBoolean();
            index = br.ReadInt32();
            if (!isRef)
            {
                int len = br.ReadInt32();
                byte[] datas = br.ReadBytes(len);
                refInfo.addr = string.Intern(Encoding.UTF8.GetString(datas));
                m_strCacheDict[index] = refInfo.addr;
            }
            else
            {
                refInfo.addr = m_strCacheDict[index];
            }

            return refInfo;
        }
    }

}
