using System;
using System.Collections.Generic;

namespace NestoAPI.Models.Mayor
{
    /// <summary>
    /// DTO para representar el Mayor completo de una cuenta (cliente o proveedor).
    /// Incluye datos de cabecera y lista de movimientos.
    /// </summary>
    public class MayorCuentaDTO
    {
        public MayorCuentaDTO()
        {
            Movimientos = new List<MovimientoMayorDTO>();
        }

        // Datos de la empresa
        public string Empresa { get; set; }
        public string NombreEmpresa { get; set; }

        // Datos de la cuenta
        public string TipoCuenta { get; set; }  // "cliente" o "proveedor"
        public string NumeroCuenta { get; set; }
        public string NombreCuenta { get; set; }
        public string CifNif { get; set; }
        public string Direccion { get; set; }

        // Periodo del Mayor
        public DateTime FechaDesde { get; set; }
        public DateTime FechaHasta { get; set; }

        // Saldo anterior (movimientos anteriores a FechaDesde)
        public decimal SaldoAnterior { get; set; }

        // Lista de movimientos
        public List<MovimientoMayorDTO> Movimientos { get; set; }

        // Totales calculados
        public decimal TotalDebe { get; set; }
        public decimal TotalHaber { get; set; }
        public decimal SaldoFinal { get; set; }

        // Indica si solo se muestran facturas (TipoApunte = 1)
        public bool SoloFacturas { get; set; }

        // Indica si se eliminaron los pares Factura+PasoACartera
        public bool EliminarPasoACartera { get; set; }
    }
}
