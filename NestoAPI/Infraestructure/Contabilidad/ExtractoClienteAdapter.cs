using NestoAPI.Models;
using NestoAPI.Models.Mayor;
using System;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Contabilidad
{
    /// <summary>
    /// Adaptador para convertir ExtractoCliente a MovimientoMayorDTO.
    ///
    /// Interpretacion del signo en ExtractoCliente:
    /// - Importe positivo = DEBE (facturas a cobrar)
    /// - Importe negativo = HABER (cobros recibidos)
    ///
    /// Esto es coherente con la naturaleza contable de clientes (cuentas 430x):
    /// son cuentas de activo con saldo deudor.
    /// </summary>
    public class ExtractoClienteAdapter : IExtractoAdapter<ExtractoCliente>
    {
        public IEnumerable<MovimientoMayorDTO> Adaptar(IEnumerable<ExtractoCliente> extractos)
        {
            foreach (var e in extractos)
            {
                yield return new MovimientoMayorDTO
                {
                    Fecha = e.Fecha,
                    Concepto = e.Concepto?.Trim(),
                    NumeroDocumento = e.Nº_Documento?.Trim(),
                    TipoApunte = e.TipoApunte?.Trim(),
                    // Positivo = Debe (facturas), Negativo = Haber (cobros)
                    Debe = e.Importe > 0 ? e.Importe : 0,
                    Haber = e.Importe < 0 ? Math.Abs(e.Importe) : 0
                };
            }
        }
    }
}
