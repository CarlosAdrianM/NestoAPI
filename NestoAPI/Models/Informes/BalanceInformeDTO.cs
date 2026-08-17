using System;
using System.Collections.Generic;

namespace NestoAPI.Models.Informes
{
    /// <summary>
    /// NestoAPI#350: balance/cuenta de resultados calculado a partir de las definiciones de las
    /// tablas Balances/LinBalance (BPY = Balance de Pymes, PGP = PyG de Pymes, y cualquier otro
    /// definido). Columnas del informe: ejercicio actual (desde/hasta), % de variación y el
    /// mismo periodo del año anterior.
    /// </summary>
    public class BalanceInformeDTO
    {
        public string Empresa { get; set; }
        public string NombreEmpresa { get; set; }
        public string Numero { get; set; }
        public string Descripcion { get; set; }
        public DateTime Desde { get; set; }
        public DateTime Hasta { get; set; }
        public List<LineaBalanceInformeDTO> Lineas { get; set; } = new List<LineaBalanceInformeDTO>();
    }

    public class LineaBalanceInformeDTO
    {
        public int Orden { get; set; }
        public string Descripcion { get; set; }
        /// <summary>'A' = panel izquierdo (activo/única columna), 'P' = panel derecho (pasivo).</summary>
        public string Tipo { get; set; }
        public int Grupo { get; set; }
        /// <summary>Línea de total (su fórmula referencia grupos, p. ej. "4+5+6+7").</summary>
        public bool EsTotal { get; set; }
        /// <summary>Epígrafe sin fórmula (título de sección): sin importes.</summary>
        public bool EsCabecera { get; set; }
        public decimal? SaldoActual { get; set; }
        public decimal? SaldoAnterior { get; set; }
        /// <summary>Variación N vs N-1 en %; null si el año anterior es 0 (sin criterio).</summary>
        public decimal? Porcentaje { get; set; }
    }
}
