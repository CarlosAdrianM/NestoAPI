using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.PedidosBase
{
    public class PedidoBase<T>
        where T : LineaPedidoBase
    {
        public PedidoBase()
        {
            Lineas = new List<T>();
        }

        // Propiedades
        public string Usuario { get; set; }

        // Propiedades Calculadas
        public decimal BaseImponible => Math.Round(Lineas.Sum(l => l.BaseImponible), 2, MidpointRounding.AwayFromZero);
        public decimal Total => Math.Round(Lineas.Sum(l => l.Total), 2, MidpointRounding.AwayFromZero);

        // Colecciones
        public virtual ICollection<T> Lineas { get; set; }
        public virtual IEnumerable<ParametrosIvaBase> ParametrosIva { get; set; }
    }
}