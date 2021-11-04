using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Reflection;
using Tbot.Includes;

namespace Tbot.Services {
	public static class SettingsService {
		public static dynamic GetSettings() {
			System.Threading.Thread.Sleep(500);
			string file = File.ReadAllText($"{Path.GetFullPath(AppContext.BaseDirectory)}/settings.json");
			dynamic settings = JsonConvert.DeserializeObject<ExpandoObject>(file, new ExpandoObjectConverter());
			settings = ConfigObject.FromExpando(JsonNetAdapter.Transform(settings));
			return settings;
		}
	}

	public static class JsonNetAdapter {
		public static ExpandoObject Transform(ExpandoObject data) {
			var newExpando = new ExpandoObject();
			var edict = (IDictionary<string, object>) newExpando;

			foreach (var kvp in data)
				edict[kvp.Key.FirstCharToUpper()] = TransformByType(kvp.Value);

			return newExpando;
		}

		private static object TransformByType(object value) {
			return value switch {
				ExpandoObject _ => Transform((ExpandoObject) value),
				List<object> _ => ConvertList((List<object>) value),
				_ => value,
			};
		}

		private static object ConvertList(List<object> list) {
			var hasSingleType = true;
			var tList = new ArrayList(list.Count);

			Type listType = null;

			if (list.Count > 0)
				listType = list.First().GetType();

			foreach (var v in list) {
				hasSingleType = hasSingleType && listType == v.GetType();
				tList.Add(TransformByType(v));
			}

			return tList.ToArray(hasSingleType && listType != null ? listType : typeof(object));
		}
	}

	public class ConfigObject : DynamicObject, IDictionary<string, object> {
		internal Dictionary<string, object> Members = new();

		#region IEnumerable implementation

		public IEnumerator GetEnumerator() {
			return Members.GetEnumerator();
		}

		#endregion

		#region IEnumerable implementation

		IEnumerator<KeyValuePair<string, object>> IEnumerable<KeyValuePair<string, object>>.GetEnumerator() {
			return Members.GetEnumerator();
		}

		#endregion

		public static ConfigObject FromExpando(ExpandoObject e) {
			var edict = e as IDictionary<string, object>;
			var c = new ConfigObject();
			var cdict = (IDictionary<string, object>) c;

			// this is not complete. It will, however work for JsonFX ExpandoObjects which consists only of primitive types,
			// ExpandoObject or ExpandoObject [] but won't work for generic ExpandoObjects which might include collections etc.
			foreach (var kvp in edict) // recursively convert and add ExpandoObjects
				switch (kvp.Value) {
					case ExpandoObject o:
						cdict.Add(kvp.Key, FromExpando(o));
						break;
					case ExpandoObject[] _:
						cdict.Add(kvp.Key, (from ex in (ExpandoObject[]) kvp.Value select FromExpando(ex)).ToArray());
						break;
					default:
						cdict.Add(kvp.Key, kvp.Value);
						break;
				}
			return c;
		}

		public override bool TryGetMember(GetMemberBinder binder, out object result) {
			result = Members.ContainsKey(binder.Name) ? Members[binder.Name] : new NullExceptionPreventer();

			return true;
		}

		public override bool TrySetMember(SetMemberBinder binder, object value) {
			if (Members.ContainsKey(binder.Name))
				Members[binder.Name] = value;
			else
				Members.Add(binder.Name, value);
			return true;
		}

		public override string ToString() {
			return JsonConvert.SerializeObject(Members);
		}

		public static implicit operator ConfigObject(ExpandoObject exp) {
			return FromExpando(exp);
		}

		#region casts

		public static implicit operator bool(ConfigObject c) {
			// we want to test for a member:
			// if (config.SomeMember) { ... }
			//
			// instead of:
			// if (config.SomeMember != null) { ... }

			// we return always true, because a NullExceptionPreventer is returned when member
			// does not exist
			return true;
		}

		#endregion

		#region ICollection implementation

		public void Add(KeyValuePair<string, object> item) {
			Members.Add(item.Key, item.Value);
		}

		public void Clear() {
			Members.Clear();
		}

		public bool Contains(KeyValuePair<string, object> item) {
			return Members.ContainsKey(item.Key) && Members[item.Key] == item.Value;
		}

		public void CopyTo(KeyValuePair<string, object>[] array, int arrayIndex) {
			throw new NotImplementedException();
		}

		public bool Remove(KeyValuePair<string, object> item) {
			throw new NotImplementedException();
		}

		public int Count => Members.Count;

		public bool IsReadOnly => throw new NotImplementedException();

		#endregion

		#region IDictionary implementation

		public void Add(string key, object value) {
			Members.Add(key, value);
		}

		public bool ContainsKey(string key) {
			return Members.ContainsKey(key);
		}

		public bool Remove(string key) {
			return Members.Remove(key);
		}

		public object this[string key] {
			get => Members[key];
			set => Members[key] = value;
		}

		public ICollection<string> Keys => Members.Keys;

		public ICollection<object> Values => Members.Values;

		public bool TryGetValue(string key, out object value) {
			return Members.TryGetValue(key, out value);
		}

		#endregion
	}

	/// <summary>
	///     Null exception preventer. This allows for hassle-free usage of configuration values that are not
	///     defined in the config file. I.e. we can do Config.Scope.This.Field.Does.Not.Exist.Ever, and it will
	///     not throw an NullPointer exception, but return te NullExceptionPreventer object instead.
	///     The NullExceptionPreventer can be cast to everything, and will then return default/empty value of
	///     that datatype.
	/// </summary>
	public class NullExceptionPreventer : DynamicObject {
		// all member access to a NullExceptionPreventer will return a new NullExceptionPreventer
		// this allows for infinite nesting levels: var s = Obj1.foo.bar.bla.blubb; is perfectly valid
		public override bool TryGetMember(GetMemberBinder binder, out object result) {
			result = new NullExceptionPreventer();
			return true;
		}

		// Add all kinds of datatypes we can cast it to, and return default values cast to string will be null
		public static implicit operator string(NullExceptionPreventer nep) {
			return null;
		}

		public override string ToString() {
			return null;
		}

		public static implicit operator string[](NullExceptionPreventer nep) {
			return Array.Empty<string>();
		}

		// cast to bool will always be false
		public static implicit operator bool(NullExceptionPreventer nep) {
			return false;
		}

		public static implicit operator bool[](NullExceptionPreventer nep) {
			return Array.Empty<bool>();
		}

		public static implicit operator int[](NullExceptionPreventer nep) {
			return Array.Empty<int>();
		}

		public static implicit operator long[](NullExceptionPreventer nep) {
			return Array.Empty<long>();
		}

		public static implicit operator int(NullExceptionPreventer nep) {
			return 0;
		}

		public static implicit operator long(NullExceptionPreventer nep) {
			return 0;
		}

		// nullable types always return null
		public static implicit operator bool?(NullExceptionPreventer nep) {
			return null;
		}

		public static implicit operator int?(NullExceptionPreventer nep) {
			return null;
		}

		public static implicit operator long?(NullExceptionPreventer nep) {
			return null;
		}
	}
}
