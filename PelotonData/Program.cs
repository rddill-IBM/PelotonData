﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft;
using Newtonsoft.Json;
using PelotonData.JSONClasses;

namespace PelotonData
{
    public class WebClientEx : WebClient
    {
        private CookieContainer _cookieContainer = new CookieContainer();

        protected override WebRequest GetWebRequest(Uri address)
        {
            WebRequest request = base.GetWebRequest(address);
            if (request is HttpWebRequest)
            {
                (request as HttpWebRequest).CookieContainer = _cookieContainer;
            }
            return request;
        }
    }
    class Program
    {
        static int ThrottleMilliseconds = 3000;

        static void Main(string[] args)
        {
            var p = new Program();
            p.Run();
        }

        void Run()
        {
            var auth = Authenticate("username", "password");
            var rideData = GetWorkoutList(auth);

            string directory = @"C:\Users\cbird\Documents\PelotonData";
            bool overwriteFiles = true;

            foreach (var ride in rideData)
            {
                try
                {
                    string filename = Path.Combine(directory, GetFileNameFromRideDatum(ride));
                    if (File.Exists(filename) && !overwriteFiles) continue;
                    var data = GetWorkoutMetrics(ride.id);
                    OutputRideCSV(data, filename);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.ToString());
                    Debug.WriteLine("\n\n*** Continuing ***");
                }
                Throttle();
            }
        }

        AuthResponse Authenticate(string user, string password)
        {
            string authURL = "https://api.pelotoncycle.com/auth/login";
            var info = new { password = password, username_or_email = user };
            string infoAsString = JsonConvert.SerializeObject(info);

            using (var client = new WebClient())
            {
                client.Headers[HttpRequestHeader.ContentType] = "application/json";
                var response = client.UploadString(authURL, infoAsString);

                Debug.WriteLine("  " + response.Substring(0, 50));
                Debug.WriteLine($"  Length: {response.Length}");

                var authResponse = JsonConvert.DeserializeObject<AuthResponse>(response);
                return authResponse;
            }
        }

        string GetFileNameFromRideDatum(RideDatum ride)
        {
            Regex rgx = new Regex("[^a-zA-Z0-9]");
            string str = rgx.Replace(ride.ride.title, "_");
            DateTime dateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc);
            dateTime = dateTime.AddSeconds(ride.device_time_created_at);
            string filename = dateTime.ToString("yyy-dd-MM_HH-mm") + "_" + str + ".csv";
            return filename;
        }

        List<RideDatum> GetWorkoutList(AuthResponse auth)
        {
            var rideDataList = new List<RideDatum>();
            string cookie = $"peloton_session_id={auth.session_id}";
            int pageNum = 0;
            while (true)
            {
                string url = $"https://api.onepeloton.com/api/user/{auth.user_id}/workouts?joins=ride&limit=10&page={pageNum}";
                using (var client = new WebClient())
                {
                    client.Headers.Add(HttpRequestHeader.Cookie, cookie);
                    client.Headers["accept"] = "application/json";
                    client.Headers["origin"] = "https://members.onepeloton.com";
                    client.Headers["accept-language"] = "en-US,en;q=0.9";
                    client.Headers["user-agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/70.0.3538.102 Safari/537.36";
                    client.Headers["accept"] = "application/json";
                    client.Headers["referer"] = "https://members.onepeloton.com/profile/workouts";
                    client.Headers["authority"] = "api.onepeloton.com";
                    client.Headers["x-requested-with"] = "XmlHttpRequest";
                    client.Headers["peloton-platform"] = "web";
                    var response1 = client.DownloadString(url);

                    Debug.WriteLine("  " + response1.Substring(0, 50));
                    Debug.WriteLine($"  Length: {response1.Length}");

                    WorkoutList workoutList = JsonConvert.DeserializeObject<WorkoutList>(response1);
                    rideDataList.AddRange(workoutList.data);

                    if (workoutList.show_next)
                    {
                        pageNum++;
                        Throttle();
                    } else
                    {
                        break;
                    }
                }
            }
            return rideDataList;
        }

        public void Throttle()
        {
            Thread.Sleep(ThrottleMilliseconds);
        }

        WorkoutSessionObject GetWorkoutMetrics(string workoutId)
        {
            using (var client = new WebClient())
            {
                client.Headers["accept"] = "application/json";
                string url = $"https://api.onepeloton.com/api/workout/{workoutId}/performance_graph?every_n=5";
                var response = client.DownloadString(url);
                Debug.WriteLine("  " + response.Substring(0, 50));
                Debug.WriteLine($"  Length: {response.Length}");

                WorkoutSessionObject session = JsonConvert.DeserializeObject<WorkoutSessionObject>(response);
                return session;
            }
        }

        void OutputRideCSV(WorkoutSessionObject session, string filename)
        {
            List<string> lines = new List<string>();
            var header = "elapsed_seconds," + string.Join(",", session.metrics.Select(m => m.slug));
            lines.Add(header);

            var metricLists = session.metrics.Select(m => m.values);
            for (int i=0; i < session.seconds_since_pedaling_start.Count(); i++)
            {
                string line = session.seconds_since_pedaling_start[i] + "," +
                    string.Join(",", metricLists.Select(m => m[i]));
                lines.Add(line);
            }
            File.WriteAllLines(filename, lines);
        }

    }
}