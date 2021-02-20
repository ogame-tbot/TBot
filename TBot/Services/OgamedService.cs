using RestSharp;
using System;
using System.Collections.Generic;
using System.Text;
using Tbot.Model;
using Newtonsoft.Json;
using System.IO;
using System.Resources;
using Tbot.Properties;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Tbot.Services
{
    class OgamedService
    {
        private string Url { get; set; }
        private RestClient Client { get; set; }

        /**
        * Tralla 20/2/21
        * 
        * add ability to set custom host 
        */
        public OgamedService(Credentials credentials, string host = "127.0.0.1", int port = 8080)
        {
            ExecuteOgamedExecutable(credentials, port);
            this.Url = "http://" + host + ":" + port;
            this.Client = new RestClient(this.Url);
        }

        internal byte[] OgamedExecutable
        {
            get
            {
                object obj;
                if (RuntimeInformation.OSArchitecture == Architecture.X64)
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        obj = Properties.Resources.ResourceManager.GetObject("ogamed_win64");
                    }
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    {
                        //obj = Properties.Resources.ResourceManager.GetObject("ogamed_linux64");
                        throw new Exception("This platform is not supported yet.");
                    }
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        //obj = Properties.Resources.ResourceManager.GetObject("ogamed_osx64");
                        throw new Exception("This platform is not supported yet.");
                    }
                    else
                    {
                        throw new Exception("This platform is not supported yet.");
                    }
                }
                else if (RuntimeInformation.OSArchitecture == Architecture.X86)
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        //obj = Properties.Resources.ResourceManager.GetObject("ogamed_win32");
                        throw new Exception("This platform is not supported yet.");
                    }
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    {
                        //obj = Properties.Resources.ResourceManager.GetObject("ogamed_linux32");
                        throw new Exception("This platform is not supported yet.");
                    }
                    else
                    {
                        throw new Exception("This platform is not supported yet.");
                    }
                }
                else if (RuntimeInformation.OSArchitecture == Architecture.Arm)
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    {
                        //obj = Properties.Resources.ResourceManager.GetObject("ogamed_arm32");
                        throw new Exception("This platform is not supported yet.");
                    }
                    else
                    {
                        throw new Exception("This platform is not supported yet.");
                    }
                }
                else if (RuntimeInformation.OSArchitecture == Architecture.Arm64)
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        //obj = Properties.Resources.ResourceManager.GetObject("ogamed_winarm64");
                        throw new Exception("This platform is not supported yet.");
                    }
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    {
                        //obj = Properties.Resources.ResourceManager.GetObject("ogamed_arm64");
                        throw new Exception("This platform is not supported yet.");
                    }
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        //obj = Properties.Resources.ResourceManager.GetObject("ogamed_osxarm64");
                        throw new Exception("This platform is not supported yet.");
                    }
                    else
                    {
                        throw new Exception("This platform is not supported yet.");
                    }
                }
                else
                {
                    throw new Exception("This platform is not supported yet.");
                }
                return ((byte[])obj);
            }
        }

        internal byte[] GetOgamedExecutable()
        {
            return OgamedExecutable;
        }

        internal void CreateOgamedExecutable()
        {
            string tempExeName = Path.Combine(Directory.GetCurrentDirectory(), "ogamed.exe");
            using FileStream fsDst = new FileStream(tempExeName, FileMode.OpenOrCreate, FileAccess.Write);
            byte[] bytes = GetOgamedExecutable();
            fsDst.Write(bytes, 0, bytes.Length);
        }

        internal void ExecuteOgamedExecutable(Credentials credentials)
        {
            ExecuteOgamedExecutable(credentials, 8080);
        }

        internal void ExecuteOgamedExecutable(Credentials credentials, int port)
        {
            CreateOgamedExecutable();
            string args = "--universe=" + credentials.Universe + " --username=" + credentials.Username + " --password=" + credentials.Password + " --language=" + credentials.Language + " --auto-login=false --port=" + port + " --api-new-hostname=http://localhost:" + port + " --cookies-filename=cookies.txt";
            Process.Start("ogamed.exe", args);
        }

        public void KillOgamedExecultable()
        {
            foreach (var process in Process.GetProcessesByName("ogamed"))
            {
                process.Kill();
            }
        }

        public bool SetUserAgent(string userAgent)
        {
            var request = new RestRequest
            {
                Resource = "/bot/set-user-agent",
                Method = Method.POST,
            };
            request.AddParameter(new Parameter("userAgent", userAgent, ParameterType.GetOrPost));
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return true;
        }

        public bool Login()
        {
            var request = new RestRequest
            {
                Resource = "/bot/login",
                Method = Method.GET
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return true;
        }

        public bool Logout()
        {
            var request = new RestRequest
            {
                Resource = "/bot/logout",
                Method = Method.GET
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return true;
        }

        public Server GetServerInfo()
        {
            var request = new RestRequest
            {
                Resource = "/bot/server",
                Method = Method.GET
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return JsonConvert.DeserializeObject<Server>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
        }

        public ServerData GetServerData()
        {
            var request = new RestRequest
            {
                Resource = "/bot/server-data",
                Method = Method.GET
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return JsonConvert.DeserializeObject<ServerData>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
        }

        public string GetServerUrl()
        {
            var request = new RestRequest
            {
                Resource = "/bot/server-url",
                Method = Method.GET
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return (string)result.Result;
        }

        public string GetServerLanguage()
        {
            var request = new RestRequest
            {
                Resource = "/bot/language",
                Method = Method.GET
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return (string)result.Result;
        }

        public string GetServerName()
        {
            var request = new RestRequest
            {
                Resource = "/bot/universe-name",
                Method = Method.GET
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return (string)result.Result;
        }

        public int GetServerSpeed()
        {
            var request = new RestRequest
            {
                Resource = "/bot/server/speed",
                Method = Method.GET
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return (int)result.Result;
        }

        public int GetServerFleetSpeed()
        {
            var request = new RestRequest
            {
                Resource = "/bot/server/speed-fleet",
                Method = Method.GET
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return (int)result.Result;
        }

        public string GetServerVersion()
        {
            var request = new RestRequest
            {
                Resource = "/bot/server/version",
                Method = Method.GET
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return (string)result.Result;
        }

        public DateTime GetServerTime()
        {
            var request = new RestRequest
            {
                Resource = "/bot/server/time",
                Method = Method.GET
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return (DateTime)result.Result;
        }

        public string GetUsername()
        {
            var request = new RestRequest
            {
                Resource = "/bot/username",
                Method = Method.GET
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return (string)result.Result;
        }

        public UserInfo GetUserInfo()
        {
            var request = new RestRequest
            {
                Resource = "/bot/user-infos",
                Method = Method.GET
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return JsonConvert.DeserializeObject<UserInfo>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
        }

        public Classes GetUserClass()
        {
            var request = new RestRequest
            {
                Resource = "/bot/user-class",
                Method = Method.GET
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return JsonConvert.DeserializeObject<Classes>(JsonConvert.SerializeObject(result.Result));
        }

        public List<Planet> GetPlanets()
        {
            var request = new RestRequest
            {
                Resource = "/bot/planets",
                Method = Method.GET
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return JsonConvert.DeserializeObject<List<Planet>>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
        }

        public Planet GetPlanet(Planet planet)
        {
            var request = new RestRequest
            {
                Resource = "/bot/planets/" + planet.ID,
                Method = Method.GET
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return JsonConvert.DeserializeObject<Planet>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
        }

        public List<Moon> GetMoons()
        {
            var request = new RestRequest
            {
                Resource = "/bot/moons",
                Method = Method.GET
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return JsonConvert.DeserializeObject<List<Moon>>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
        }

        public Moon GetMoon(Moon moon)
        {
            var request = new RestRequest
            {
                Resource = "/bot/moons/" + moon.ID,
                Method = Method.GET
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return JsonConvert.DeserializeObject<Moon>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
        }

        public List<Celestial> GetCelestials()
        {
            var planets = this.GetPlanets();
            var moons = this.GetMoons();
            List<Celestial> celestials = new List<Celestial>();
            celestials.AddRange(planets);
            celestials.AddRange(moons);
            return celestials;
        }

        public Celestial GetCelestial(Celestial celestial)
        {
            if (celestial is Moon)
                return this.GetMoon(celestial as Moon);
            else if (celestial is Planet)
                return this.GetPlanet(celestial as Planet);
            else return celestial;
        }

        public Model.Resources GetResources(Celestial celestial)
        {
            var request = new RestRequest
            {
                Resource = "/bot/planets/" + celestial.ID + "/resources",
                Method = Method.GET,
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return JsonConvert.DeserializeObject<Model.Resources>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
        }

        public Buildings GetBuildings(Celestial celestial)
        {
            var request = new RestRequest
            {
                Resource = "/bot/planets/" + celestial.ID + "/resources-buildings",
                Method = Method.GET,
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return JsonConvert.DeserializeObject<Model.Buildings>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
        }

        public Facilities GetFacilities(Celestial celestial)
        {
            var request = new RestRequest
            {
                Resource = "/bot/planets/" + celestial.ID + "/facilities",
                Method = Method.GET,
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return JsonConvert.DeserializeObject<Facilities>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
        }

        public Defences GetDefences(Celestial celestial)
        {
            var request = new RestRequest
            {
                Resource = "/bot/planets/" + celestial.ID + "/defence",
                Method = Method.GET,
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return JsonConvert.DeserializeObject<Defences>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
        }

        public Ships GetShips(Celestial celestial)
        {
            var request = new RestRequest
            {
                Resource = "/bot/planets/" + celestial.ID + "/ships",
                Method = Method.GET,
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return JsonConvert.DeserializeObject<Ships>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
        }

        public bool IsUnderAttack()
        {
            var request = new RestRequest
            {
                Resource = "/bot/is-under-attack",
                Method = Method.GET
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return (bool)result.Result;
        }

        public bool IsVacationMode()
        {
            var request = new RestRequest
            {
                Resource = "/bot/is-vacation-mode",
                Method = Method.GET
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return (bool)result.Result;
        }

        public List<AttackerFleet> GetAttacks()
        {
            var request = new RestRequest
            {
                Resource = "/bot/attacks",
                Method = Method.GET
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return JsonConvert.DeserializeObject<List<AttackerFleet>>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
        }

        public List<Fleet> GetFleets()
        {
            var request = new RestRequest
            {
                Resource = "/bot/fleets",
                Method = Method.GET
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return JsonConvert.DeserializeObject<List<Fleet>>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
        }

        public Slots GetSlots()
        {
            var request = new RestRequest
            {
                Resource = "/bot/fleets/slots",
                Method = Method.GET,
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return JsonConvert.DeserializeObject<Slots>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
        }

        public Researches GetResearches()
        {
            var request = new RestRequest
            {
                Resource = "/bot/get-research",
                Method = Method.GET,
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return JsonConvert.DeserializeObject<Researches>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
        }

        public List<Production> GetProductions(Celestial celestial)
        {
            var request = new RestRequest
            {
                Resource = "/bot/planets/" + celestial.ID + "/production",
                Method = Method.GET,
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return JsonConvert.DeserializeObject<List<Production>>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
        }

        public ResourceSettings GetResourceSettings(Planet planet)
        {
            var request = new RestRequest
            {
                Resource = "/bot/planets/" + planet.ID + "/resource-settings",
                Method = Method.GET,
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return JsonConvert.DeserializeObject<ResourceSettings>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
        }

        public Model.Resources GetResourceProduction(Planet planet)
        {
            var request = new RestRequest
            {
                Resource = "/bot/planets/" + planet.ID + "/resource-production",
                Method = Method.GET,
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return JsonConvert.DeserializeObject<Model.Resources>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
        }

        public Constructions GetConstructions(Celestial celestial)
        {
            var request = new RestRequest
            {
                Resource = "/bot/planets/" + celestial.ID + "/constructions",
                Method = Method.GET,
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return JsonConvert.DeserializeObject<Constructions>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
        }

        public bool CancelConstruction(Celestial celestial)
        {
            var request = new RestRequest
            {
                Resource = "/bot/planets/" + celestial.ID + "/cancel-building",
                Method = Method.POST,
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return true;
        }

        public bool CancelResearch(Celestial celestial)
        {
            var request = new RestRequest
            {
                Resource = "/bot/planets/" + celestial.ID + "/cancel-research",
                Method = Method.POST,
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return true;
        }

        public Fleet SendFleet(Celestial origin, Ships ships, Coordinate destination, Missions mission, Speeds speed, Model.Resources payload)
        {
            var request = new RestRequest
            {
                Resource = "/bot/planets/" + origin.ID + "/send-fleet",
                Method = Method.POST,
            };

            request.AddParameter(new Parameter("galaxy", destination.Galaxy, ParameterType.GetOrPost));
            request.AddParameter(new Parameter("system", destination.System, ParameterType.GetOrPost));
            request.AddParameter(new Parameter("position", destination.Position, ParameterType.GetOrPost));
            request.AddParameter(new Parameter("type", (int)destination.Type, ParameterType.GetOrPost));

            foreach (PropertyInfo prop in ships.GetType().GetProperties())
            {
                long qty = (long)prop.GetValue(ships, null);
                if (qty == 0) continue;
                if (Enum.TryParse<Buildables>(prop.Name, out Buildables buildable))
                {
                    request.AddParameter(new Parameter("ships", (int)buildable + "," + prop.GetValue(ships, null), ParameterType.GetOrPost));
                }
            }

            request.AddParameter(new Parameter("mission", (int)mission, ParameterType.GetOrPost));

            request.AddParameter(new Parameter("speed", (int)speed, ParameterType.GetOrPost));

            request.AddParameter(new Parameter("metal", payload.Metal, ParameterType.GetOrPost));
            request.AddParameter(new Parameter("crystal", payload.Crystal, ParameterType.GetOrPost));
            request.AddParameter(new Parameter("deuterium", payload.Deuterium, ParameterType.GetOrPost));

            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return JsonConvert.DeserializeObject<Fleet>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
        }

        public bool CancelFleet(Fleet fleet)
        {
            var request = new RestRequest
            {
                Resource = "/bot/fleets/" + fleet.ID + "/cancel",
                Method = Method.POST,
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return true;
        }

        public GalaxyInfo GetGalaxyInfo(Coordinate coordinate)
        {
            var request = new RestRequest
            {
                Resource = "/bot/galaxy-infos/" + coordinate.Galaxy + "/" + coordinate.System,
                Method = Method.GET,
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return JsonConvert.DeserializeObject<GalaxyInfo>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
        }

        public GalaxyInfo GetGalaxyInfo(int galaxy, int system)
        {
            Coordinate coordinate = new Coordinate { Galaxy = galaxy, System = system };
            return this.GetGalaxyInfo(coordinate);
        }

        public bool BuildCancelable(Celestial celestial, Buildables buildable)
        {
            var request = new RestRequest
            {
                Resource = "/bot/planets/" + celestial.ID + "/build/cancelable/" + (int)buildable,
                Method = Method.POST,
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return true;
        }

        public bool BuildConstruction(Celestial celestial, Buildables buildable)
        {
            var request = new RestRequest
            {
                Resource = "/bot/planets/" + celestial.ID + "/build/building/" + (int)buildable,
                Method = Method.POST,
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return true;
        }

        public bool BuildTechnology(Celestial celestial, Buildables buildable)
        {
            var request = new RestRequest
            {
                Resource = "/bot/planets/" + celestial.ID + "/build/technology/" + (int)buildable,
                Method = Method.POST,
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return true;
        }

        public bool BuildMilitary(Celestial celestial, Buildables buildable, long quantity)
        {
            var request = new RestRequest
            {
                Resource = "/bot/planets/" + celestial.ID + "/build/production/" + (int)buildable + "/" + quantity,
                Method = Method.POST,
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return true;
        }

        public bool BuildShips(Celestial celestial, Buildables buildable, long quantity)
        {
            var request = new RestRequest
            {
                Resource = "/bot/planets/" + celestial.ID + "/build/ships/" + (int)buildable + "/" + quantity,
                Method = Method.POST,
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return true;
        }

        public bool BuildDefences(Celestial celestial, Buildables buildable, long quantity)
        {
            var request = new RestRequest
            {
                Resource = "/bot/planets/" + celestial.ID + "/build/defence/" + (int)buildable + "/" + quantity,
                Method = Method.POST,
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return true;
        }

        public Model.Resources GetPrice(Buildables buildable, long levelOrQuantity)
        {
            var request = new RestRequest
            {
                Resource = "/bot/price/" + (int)buildable + "/" + levelOrQuantity,
                Method = Method.GET,
            };
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return JsonConvert.DeserializeObject<Model.Resources>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings { DateTimeZoneHandling = DateTimeZoneHandling.Local });
        }

        public bool SendMessage(int playerID, string message)
        {
            var request = new RestRequest
            {
                Resource = "/bot/send-message",
                Method = Method.POST,
            };
            request.AddParameter(new Parameter("playerID", playerID, ParameterType.GetOrPost));
            request.AddParameter(new Parameter("message", message, ParameterType.GetOrPost));
            var result = JsonConvert.DeserializeObject<OgamedResponse>(Client.Execute(request).Content);
            if (result.Status != "ok")
            {
                throw new Exception("An error has occurred: Status: " + result.Status + " - Message: " + result.Message);
            }
            else return true;
        }
    }
}
