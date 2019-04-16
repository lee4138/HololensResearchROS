using System;
using UnityEngine;
using System.Threading;
using System.Collections.Generic;
namespace RosSharp.RosBridgeClient
{

    [RequireComponent(typeof(MeshRenderer))]
    class DepthPublisher2 : MessageProvider
    {
        
        private SensorPointCloud2 message;
        private int messageLength;
        public override Type MessageType { get { return (typeof(SensorPointCloud2)); } }
        public List<float> DepthList = new List<float>();
        private byte[] DepthArray;





        private void Start()
        {

            

            //message.pose.position = GetGeometryPoint(transform.position.Unity2Ros());
            //message.pose.orientation = GetGeometryQuaternion(transform.rotation.Unity2Ros());

        }

        private void Awake()
        {
            DepthProvider.DepthProvided += OnDepthProvided; //subscribe to objectinstantiated event 
        }

        public void OnDepthProvided(object source, DepthProvider.SensorEventArgs e)
        {
            
            messageLength = e.depthInfos.Count;
            DepthList = e.depthInfos;
            InitializeMessage();


            //make sure to only send if depth data has arrived

            for (int i = 0; i < messageLength; i++)
            {
                DepthArray[i] = (byte)DepthList[i];

            }

            message.data = DepthArray;
            RaiseMessageRelease(new MessageEventArgs(message));

        }
        private void Update()
        {

         //UpdateMessage();
           

        }

        private void UpdateMessage()
        {
            message.header.Update();
            for(int i = 0; i < messageLength; i++)
            {
                UpdateDepthArray(i);
            }

            message.data = DepthArray;
            RaiseMessageRelease(new MessageEventArgs(message));
           


        }
           

        private void InitializeMessage()
        {
            message = new SensorPointCloud2();
            message.header = new StandardHeader();
            message.data = new byte[messageLength];
            DepthArray = new byte[messageLength];
            
        }

        private void UpdateDepthArray(int i)
        {
            //message.data[i] = (byte)DepthList[i];   
            DepthArray[i] = (byte)DepthList[i];
            
        }

    }

    

}



