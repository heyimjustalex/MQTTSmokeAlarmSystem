﻿using Client.SensorBase;
using System.Threading;
using Python.Runtime;

namespace Client.Sensors
{
    internal class SmokeDetectorMcp3008 : ISensorGetData
    {
        dynamic mcp;
        float threshold = 1f;

        public SmokeDetectorMcp3008(){
            threshold = float.Parse(Environment.GetEnvironmentVariable("BUZZER_THRESHOLD"));
            Console.WriteLine("Alarm state threshold: " + threshold.ToString());
            PythonEngine.Initialize();
            using(Py.GIL()){
                dynamic gpiozero = Py.Import("gpiozero");
                mcp = gpiozero.MCP3008();
            }
        }

        public SensorData get()
        {
            float val;
            using(Py.GIL()){
                val = mcp.value;
            }

            Console.WriteLine("Mcp3008 data:");
            Console.WriteLine(val.ToString());

            string isThereSmoke = "FALSE";
            if(val > threshold){
                isThereSmoke = "TRUE";
            }

            return new SensorData("SMOKE", isThereSmoke);
            
        }
    }
}
