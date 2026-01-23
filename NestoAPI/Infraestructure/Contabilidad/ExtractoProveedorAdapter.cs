using NestoAPI.Models;
using NestoAPI.Models.Mayor;
using System;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Contabilidad
{
    /// <summary>
    /// Adaptador para convertir ExtractoProveedor a MovimientoMayorDTO.
    ///
    /// Interpretacion del signo en ExtractoProveedor (INVERSO a clientes):
    /// - Importe positivo = HABER (facturas de proveedor a pagar)
    /// - Importe negativo = DEBE (pagos realizados)
    ///
    /// Esto es coherente con la naturaleza contable de proveedores (cuentas 400x):
    /// son cuentas de pasivo con saldo acreedor.
    /// </summary>
    public class ExtractoProveedorAdapter : IExtractoAdapter<ExtractoProveedor>
    {
        public IEnumerable<MovimientoMayorDTO> Adaptar(IEnumerable<ExtractoProveedor> extractos)
        {
            foreach (var e in extractos)
            {
                yield return new MovimientoMayorDTO
                {
                    Fecha = e.Fecha,
                    Concepto = e.Concepto?.Trim(),
                    NumeroDocumento = e.NºDocumento?.Trim(),
                    TipoApunte = e.TipoApunte?.Trim(),
                    // Positivo = Haber (facturas proveedor), Negativo = Debe (pagos)
                    // INVERSO a clientes
                    Debe = e.Importe < 0 ? Math.Abs(e.Importe) : 0,
                    Haber = e.Importe > 0 ? e.Importe : 0
                };
            }
        }
    }
}
