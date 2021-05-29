using UnityEngine;

namespace BeatSaberCinema
{
	//Yoinked from Caeden117's Counters+ (with permission)
	//Modified to play nice with nullability and to satisfy the editorconfig
	//Original at https://github.com/Caeden117/CountersPlus/blob/4f80677dffe5b43f9d8cb2b7a38fae2ea38ef58f/Counters%2B/Utils/SoftParent.cs
	public class SoftParent : MonoBehaviour
	{
		private Transform? _parent;

		private Vector3 _posOffset;
		private Quaternion _rotOffset;

		private void Update()
		{
			if (_parent == null)
			{
				return;
			}

			var transform1 = transform ;
			transform1.SetPositionAndRotation(_parent.position, _parent.rotation);
			var side = _parent.right * _posOffset.x;
			var forward = _parent.forward * _posOffset.z;
			var total = side + forward;
			total = new Vector3(total.x, _posOffset.y, total.z);
			transform1.position -= total;
			transform.rotation *= Quaternion.Inverse(_rotOffset);
		}

		public void AssignParent(Transform? newParent)
		{
			_parent = newParent;
			if (_parent == null)
			{
				return;
			}

			var transform1 = transform;
			_posOffset = _parent.position - transform1.position;
			_rotOffset = _parent.rotation * Quaternion.Inverse(transform1.rotation);
		}
	}
}