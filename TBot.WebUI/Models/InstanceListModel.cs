namespace TBot.WebUI.Models {
	public class InstanceListModel {
		public InstanceListModel() {
			Instances = new List<InstanceModel>();
		}
		public List<InstanceModel> Instances { get; set; }
	}
}
