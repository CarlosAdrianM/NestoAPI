using System.Collections.Generic;

namespace NestoAPI.Models.Comisiones.Estetica
{
    public class ComisionesAnualesTelefono2026 : ComisionesAnualesTelefono2025
    {
        /// <summary>
        /// Factor de incremento IPC para 2026 (2.7%)
        /// </summary>
        private const decimal FACTOR_IPC_2026 = 1.027M;

        public ComisionesAnualesTelefono2026(IServicioComisionesAnuales servicioComisionesVentas)
            : base(servicioComisionesVentas)
        {
        }

        public override ICollection<TramoComision> LeerTramosBase()
        {
            var tramosAnteriores = base.LeerTramosBase();
            return ReescalarTramos(tramosAnteriores, FACTOR_IPC_2026);
        }
    }
}
