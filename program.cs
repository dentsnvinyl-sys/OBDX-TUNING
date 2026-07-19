using System;
using System.IO.Ports;
using System.Threading;

namespace ODBX
{
    class Program
    {
        private const string ObdxPortName = "COM10";
        private static SerialPort _port;

        static void Main (string[] args)
        {
            try
            {
                OpenDviPort();

                Console.WriteLine("OBDX VX connected via DVI on " + ObdxPortName);
                Console.WriteLine();

                UnlockECM();
                ReadE37InfoSheet();   // <--- ADDED CORRECTLY
                ReadOsId();
                ReadVin();
                ReadDtcs();
                ClearDtcs();
                StartPidLogging();

                Console.WriteLine();
                Console.WriteLine("Starting live PID logging. Press Ctrl+C to stop.");
                StartPidLogging();
            }
            catch(Exception ex)
            {
                Console.WriteLine("ERROR: " + ex.Message);
            }

            Console.WriteLine("Press any key to exit.");
            Console.ReadKey();

            CloseDviPort();
        }

        private static void OpenDviPort ()
        {
            _port = new SerialPort(ObdxPortName, 115200, Parity.None, 8, StopBits.One);
            _port.NewLine = "\r";
            _port.ReadTimeout = 1000;
            _port.WriteTimeout = 1000;
            _port.Open();

            SendDvi("ATZ");
            Thread.Sleep(1200);

            SendDvi("ATE0");
            Thread.Sleep(150);

            SendDvi("ATSP6");
            Thread.Sleep(150);

            SendDvi("ATSH7E0");
            Thread.Sleep(150);

            _port.DiscardInBuffer();
        }

        private static void CloseDviPort ()
        {
            try
            {
                if(_port != null && _port.IsOpen)
                    _port.Close();
            }
            catch { }
        }

        private static void SendDvi (string cmd)
        {
            if(_port == null || !_port.IsOpen)
                throw new Exception("DVI port not open.");

            _port.WriteLine(cmd);
        }

        private static string ReadDviLine ()
        {
            try
            {
                return _port.ReadLine().Trim();
            }
            catch(TimeoutException)
            {
                return string.Empty;
            }
        }

        private static void UnlockECM ()
        {
            Console.WriteLine("Entering diagnostic session...");
            SendDvi("1003");
            Thread.Sleep(150);

            SendDvi("ATSH7E0");
            Thread.Sleep(100);

            Console.WriteLine("Requesting security seed...");
            _port.DiscardInBuffer();
            SendDvi("2701");

            string seedLine = "";

            for(int i = 0; i < 20; i++)
            {
                string line = ReadDviLine();
                if(!string.IsNullOrWhiteSpace(line))
                    Console.WriteLine("SECURITY LINE: " + line);

                if(line.StartsWith("67 01"))
                {
                    seedLine = line;
                    break;
                }
            }

            Console.WriteLine("RAW Seed Response: " + seedLine);

            if(string.IsNullOrWhiteSpace(seedLine))
            {
                Console.WriteLine("Failed to parse seed.");
                return;
            }

            string[] parts = seedLine.Split(' ');
            byte seedHigh = Convert.ToByte(parts[2], 16);
            byte seedLow = Convert.ToByte(parts[3], 16);

            Console.WriteLine($"Seed: {seedHigh:X2}{seedLow:X2}");

            byte[] seedBytes = new byte[] { seedHigh, seedLow };
            byte[] keyBytes = SecurityKey.Calculate(seedBytes);

            Console.WriteLine($"Key: {keyBytes[0]:X2}{keyBytes[1]:X2}");

            string unlockCmd = "2702" + keyBytes[0].ToString("X2") + keyBytes[1].ToString("X2");
            Console.WriteLine("Sending unlock: " + unlockCmd);

            Thread.Sleep(3000); // Required delay

            SendDvi(unlockCmd);

            string unlockResp = "";

            for(int i = 0; i < 20; i++)
            {
                string line = ReadDviLine();
                if(!string.IsNullOrWhiteSpace(line))
                {
                    Console.WriteLine("UNLOCK LINE: " + line);

                    if(line.StartsWith("67 02") || line.StartsWith("7F 27"))
                    {
                        unlockResp = line;
                        break;
                    }
                }
            }

            Console.WriteLine("Unlock Response: " + unlockResp);
        }

        private static void ReadVin ()
        {
            Console.WriteLine("Reading VIN via DVI...");
            SendDvi("0902");

            for(int i = 0; i < 20; i++)
            {
                string line = ReadDviLine();
                if(!string.IsNullOrWhiteSpace(line))
                    Console.WriteLine("RAW VIN: " + line);
            }
        }

        private static void ReadDtcs ()
        {
            Console.WriteLine("Reading DTCs via DVI...");
            SendDvi("03");

            for(int i = 0; i < 20; i++)
            {
                string line = ReadDviLine();
                if(!string.IsNullOrWhiteSpace(line))
                    Console.WriteLine("RAW DTC: " + line);
            }
        }

        private static void ClearDtcs ()
        {
            Console.WriteLine("Clearing DTCs via DVI...");
            SendDvi("04");

            string resp = ReadDviLine();
            Console.WriteLine("Clear DTC response: " + resp);
        }

        private static void StartPidLogging ()
        {
            while(true)
            {
                double rpm = QueryPidRpm();
                double speed = QueryPidSpeed();
                double throttle = QueryPidThrottle();
                double map = QueryPidMap();
                double boost = map - 100.0;

                Console.WriteLine(
                    $"RPM: {rpm:F0} | Speed: {speed:F0} km/h | Throttle: {throttle:F1}% | MAP: {map:F1} kPa | Boost: {boost:F1} kPa");

                Thread.Sleep(300);
            }
        }

        private static double QueryPidRpm ()
        {
            SendDvi("010C");
            string line = ReadDviLine();
            return DecodeTwoBytePid(line, 4.0);
        }

        private static double QueryPidSpeed ()
        {
            SendDvi("010D");
            string line = ReadDviLine();
            return DecodeSingleBytePid(line);
        }

        private static double QueryPidThrottle ()
        {
            SendDvi("0111");
            string line = ReadDviLine();
            double raw = DecodeSingleBytePid(line);
            return (raw * 100.0) / 255.0;
        }

        private static double QueryPidMap ()
        {
            SendDvi("010B");
            string line = ReadDviLine();
            return DecodeSingleBytePid(line);
        }

        private static double DecodeSingleBytePid (string line)
        {
            try
            {
                if(string.IsNullOrWhiteSpace(line))
                    return 0;

                Console.WriteLine("RAW PID: " + line);

                if(line.Contains("7F"))
                    return 0;

                string[] parts = line.Split(' ');
                if(parts.Length < 3)
                    return 0;

                if(parts[0] != "41")
                    return 0;

                byte A = Convert.ToByte(parts[2], 16);
                return A;
            }
            catch
            {
                return 0;
            }
        }

        private static double DecodeTwoBytePid (string line, double divisor)
        {
            try
            {
                if(string.IsNullOrWhiteSpace(line))
                    return 0;

                Console.WriteLine("RAW PID: " + line);

                if(line.Contains("7F"))
                    return 0;

                string[] parts = line.Split(' ');
                if(parts.Length < 4)
                    return 0;

                if(parts[0] != "41")
                    return 0;

                byte A = Convert.ToByte(parts[2], 16);
                byte B = Convert.ToByte(parts[3], 16);
                int raw = (A * 256) + B;
                return raw / divisor;
            }
            catch
            {
                return 0;
            }
        }
        private static void ReadOsId ()
        {
            Console.WriteLine("Reading OS ID via DVI...");
            SendDvi("0904");

            for(int i = 0; i < 20; i++)
            {
                string line = ReadDviLine();
                if(!string.IsNullOrWhiteSpace(line))
                    Console.WriteLine("RAW OSID: " + line);
            }
        }


        // ============================================================
        // E37 INFO SHEET (CORRECTLY PLACED INSIDE CLASS)
        // ============================================================

        private static void ReadE37InfoSheet ()
        {
            Console.WriteLine();
            Console.WriteLine("=== READING E37 ECU INFORMATION ===");

            SendDvi("ATSH7E0");
            Thread.Sleep(100);

            ReadDid("Calibration ID", "F190");
            ReadDid("CVN", "F18C");
            ReadDid("ECU-NR PROD", "F187");
            ReadDid("ECU-NR ECU", "F188");
            ReadDid("Software Number", "F189");
            ReadDid("Software Version", "F18A");
            ReadDid("Checksum 8-bit", "F18B");

            Console.WriteLine("Producer: Delphi");
            Console.WriteLine("Processor: Freescale/NXP MPC5554");
        }

        private static void ReadDid (string label, string did)
        {
            Console.WriteLine($"Requesting {label} (22{did})...");
            SendDvi("22" + did);

            for(int i = 0; i < 20; i++)
            {
                string line = ReadDviLine();
                if(!string.IsNullOrWhiteSpace(line))
                {
                    Console.WriteLine($"{label} RAW: {line}");

                    if(line.StartsWith("62"))
                    {
                        Console.WriteLine($"{label}: {line}");
                        break;
                    }
                }
            }
        }
    }
}
