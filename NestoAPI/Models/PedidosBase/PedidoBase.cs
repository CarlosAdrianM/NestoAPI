﻿using System;
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
        private decimal _descuentoEntidad;
        public decimal DescuentoEntidad
        {
            get => _descuentoEntidad;
            set
            {
                _descuentoEntidad = value;
                foreach(var l in Lineas)
                {
                    l.DescuentoEntidad = value;
                }
            }
        }
        private decimal _descuentoPP;
        public decimal DescuentoPP
        {
            get => _descuentoPP;
            set
            {
                _descuentoPP = value;
                foreach(var l in Lineas)
                {
                    l.DescuentoPP = value;
                }
            }
        }
        public string Usuario { get; set; }

        // Propiedades Calculadas
        public decimal BaseImponible => Math.Round(Lineas.Sum(l => l.BaseImponible), 2, MidpointRounding.AwayFromZero);
        public decimal Total => Math.Round(Lineas.Sum(l => l.Total), 2, MidpointRounding.AwayFromZero);

        // Colecciones
        public virtual ICollection<T> Lineas { get; set; }
        public virtual IEnumerable<ParametrosIvaBase> ParametrosIva { get; set; }
    }
}