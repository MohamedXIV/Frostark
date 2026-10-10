using System.Collections.Generic;
using System.IO;
using FrostArk.Core.Crew;
using FrostArk.Core.World;

namespace FrostArk.Core.Save
{
    public static partial class SaveCodec
    {
        /// <summary>
        /// v11 appends approved treatment item definitions after v10 combat,
        /// preserving the old crew member/wounds layout.
        /// </summary>
        private static void WriteMedical(BinaryWriter writer, CrewState crew)
        {
            WriteCount(writer, crew.Treatments.Count);
            foreach (var t in crew.Treatments)
            {
                WriteId(writer, t.ItemId);
                writer.Write(t.Version);
                writer.Write(t.HealPermille);
                writer.Write(t.MinimumMedicalSkill);
            }
        }

        private static IReadOnlyList<MedicalTreatmentProtocol> ReadMedical(BinaryReader reader)
        {
            var treatments = new List<MedicalTreatmentProtocol>();
            for (int i = 0, count = ReadCount(reader); i < count; i++)
            {
                var item = ReadId(reader);
                int version = reader.ReadInt32();
                int healing = reader.ReadInt32();
                int medicalSkill = reader.ReadInt32();
                treatments.Add(new MedicalTreatmentProtocol(item, healing,
                    medicalSkill, version));
            }
            return treatments;
        }
    }
}
