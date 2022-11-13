using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TBot.Ogame.Infrastructure.Enums;

namespace TBot.Ogame.Infrastructure.Models {
	public class Fleet {
		public Missions Mission { get; set; }
		public bool ReturnFlight { get; set; }
		public bool InDeepSpace { get; set; }
		public int ID { get; set; }
		public Resources Resources { get; set; }
		public Coordinate Origin { get; set; }
		public Coordinate Destination { get; set; }
		public Ships Ships { get; set; }
		public DateTime StartTime { get; set; }
		public DateTime ArrivalTime { get; set; }
		public DateTime? BackTime { get; set; }
		public int ArriveIn { get; set; }
		public int? BackIn { get; set; }
		public int? UnionID { get; set; }
		public int TargetPlanetID { get; set; }
	}

}
