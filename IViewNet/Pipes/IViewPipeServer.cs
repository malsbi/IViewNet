﻿using IViewNet.Common;
using IViewNet.Common.Models;
using System;
using System.IO;
using System.IO.Pipes;

namespace IViewNet.Pipes
{
    public class IViewPipeServer
    {
        #region "Private Fields"
        private PipeConfig PipeServerConfig;
        private MemoryStream MessageStore;
        private NamedPipeServerStream Pipe;
        private byte[] InnerBuffer;
        private object ShutdownLock;
        #endregion

        #region "Properties"
        public bool IsPipeConnected
        {
            get
            {
                if (Pipe != null)
                {
                    return Pipe.IsConnected;
                }
                return false;
            }
        }
        public PacketManager PacketManager { get; set; }
        public bool IsPipeShutdown { get; private set; }
        #endregion
        
        #region "Events"
        public delegate void PipeConnectedDelegate();
        public event PipeConnectedDelegate PipeConnectedEvent;

        public delegate void PipeReceivedDelegate(Packet Message);
        public event PipeReceivedDelegate PipeReceivedEvent;

        public delegate void PipeSentDelegate(Packet Message);
        public event PipeSentDelegate PipeSentEvent;

        public delegate void PipeClosedDelegate();
        public event PipeClosedDelegate PipeClosedEvent;

        public delegate void PipeExpectionDelegate(Exception Error);
        public event PipeExpectionDelegate PipeExceptionEvent;
        #endregion

        #region "Events Handlers"
        private void SetOnPipeConnected()
        {
            PipeConnectedEvent?.Invoke();
        }
        private void SetOnPipeClosed()
        {
            PipeClosedEvent?.Invoke();
        }
        private void SetOnPipeReceived(Packet Message)
        {
            PipeReceivedEvent?.Invoke(Message);
        }
        private void SetOnPipeSent(Packet Message)
        {
            PipeSentEvent?.Invoke(Message);
        }
        private void SetOnPipeException(Exception Error)
        {
            if (IsPipeShutdown == false)
            {
                PipeExceptionEvent?.Invoke(Error);
            }
        }
        #endregion

        public IViewPipeServer(PipeConfig PipeServerConfig)
        {
            this.PipeServerConfig = PipeServerConfig;
            Initialize();
        }

        public void StartPipeServer()
        {
            try
            {
                Pipe.BeginWaitForConnection(new AsyncCallback(WaitForConnectionCallBack), null);
            }
            catch (Exception ex)
            {
                SetOnPipeException(ex);
                ClosePipeServer();
            }
        }
        public void ClosePipeServer()
        {
            lock (ShutdownLock)
            {
                if (IsPipeShutdown == false)
                {
                    if (IsPipeConnected)
                    {
                        Pipe.Disconnect();
                    }
                    Pipe.Close();
                    Pipe.Dispose();
                    if (PacketManager != null)
                    {
                        PacketManager.Dispose();
                    }
                    IsPipeShutdown = true;
                    SetOnPipeClosed();
                }
            }
        }
        public void SendMessage(Packet Message)
        {
            if (Pipe.IsConnected)
            {
                byte[] MessageBytes = Message.ToPacket();
                Pipe.Write(MessageBytes, 0, MessageBytes.Length);
                Pipe.Flush();
                SetOnPipeSent(Message);
            }
        }

        #region "Private Methods"
        private void Initialize()
        {
            Pipe = new NamedPipeServerStream("IViewServerManager", PipeDirection.InOut, PipeServerConfig.GetMaxNumOfServers(), PipeTransmissionMode.Message, PipeOptions.Asynchronous);
            PacketManager = new PacketManager();
            MessageStore = new MemoryStream();
            InnerBuffer = new byte[PipeServerConfig.GetBufferSize()];
            ShutdownLock = new object();
            IsPipeShutdown = false;
        }

        private void WaitForConnectionCallBack(IAsyncResult Ar)
        {
            try
            {
                Pipe.EndWaitForConnection(Ar);
                if (IsPipeConnected)
                {
                    SetOnPipeConnected();
                    StartReceivingData();
                }
            }
            catch (Exception ex)
            {
                SetOnPipeException(ex);
                ClosePipeServer();
            }
        }

        private void StartReceivingData()
        {
            if (IsPipeConnected)
            {
                try
                {
                    Pipe.BeginRead(InnerBuffer, 0, InnerBuffer.Length, new AsyncCallback(StartReceivingDataCallBack), null);
                }
                catch (Exception ex)
                {
                    SetOnPipeException(ex);
                    ClosePipeServer();
                }
            }
        }

        private void StartReceivingDataCallBack(IAsyncResult Ar)
        {
            try
            {
                int BytesReceived = Pipe.EndRead(Ar);

                if (BytesReceived <= 0)
                {
                    Console.WriteLine("LESS THAN ZERO");
                    ClosePipeServer();
                }
                else
                {
                    MessageStore.Write(InnerBuffer, 0, BytesReceived);
                    if (Pipe.IsMessageComplete)
                    {
                        Packet Message = PacketManager.GetPacket(MessageStore.ToArray(), 0);
                        SetOnPipeReceived(Message);
                        MessageStore.Position = 0;
                        MessageStore.SetLength(0);
                    }
                    StartReceivingData();
                }
            }
            catch (Exception ex)
            {
                SetOnPipeException(ex);
                ClosePipeServer();
            }
        }
        #endregion
    }
}