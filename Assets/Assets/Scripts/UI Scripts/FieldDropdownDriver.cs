using TMPro;
using UnityEngine;

namespace WorldSim
{
    // When the dropdown changes, sets the alternate field on one FieldViewToggle.
    // If fieldNames is filled in, option i uses fieldNames[i]; otherwise the option's text is used.
    public class FieldDropdownDriver : MonoBehaviour
    {
        [SerializeField] TMP_Dropdown dropdown;
        [SerializeField] FieldViewToggle targetToggle;
        [SerializeField] string[] fieldNames; // optional, matches dropdown option order

        void OnEnable()
        {
            dropdown.onValueChanged.AddListener(OnDropdownChanged);
            OnDropdownChanged(dropdown.value); // apply the starting selection
        }

        void OnDisable()
        {
            dropdown.onValueChanged.RemoveListener(OnDropdownChanged);
        }

        void OnDropdownChanged(int index)
        {
            string field = (fieldNames != null && index < fieldNames.Length)
                ? fieldNames[index]
                : dropdown.options[index].text;

            targetToggle.ChangeFieldFilter(field);
        }
    }
}
