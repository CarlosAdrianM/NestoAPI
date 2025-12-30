using System.Collections.Generic;

namespace NestoAPI.Models.Comisiones
{
    public class ComisionesAnualesMinivendedores2026 : ComisionesAnualesMinivendedores2025
    {
        /// <summary>
        /// Factor de incremento IPC para 2026 (2.7%)
        /// </summary>
        private const decimal FACTOR_IPC_2026 = 1.027M;

        public ComisionesAnualesMinivendedores2026(IServicioComisionesAnuales servicioComisiones)
            : base(servicioComisiones)
        {
        }

        public override ICollection<TramoComision> LeerTramosBase()
        {
            var tramosAnteriores = base.LeerTramosBase();
            return ReescalarTramos(tramosAnteriores, FACTOR_IPC_2026);
        }
    }
}
