using NestoAPI.Models.Mayor;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Contabilidad
{
    /// <summary>
    /// Calcula saldos y procesa movimientos para el Mayor de cuentas.
    /// </summary>
    public static class CalculadorSaldoMayor
    {
        /// <summary>
        /// Convierte el saldo anterior de ExtractoProveedor al formato Debe-Haber.
        /// En ExtractoProveedor: positivo = factura (Haber), negativo = pago (Debe).
        /// Para el calculo de saldo acumulado (Debe - Haber), necesitamos invertir el signo.
        /// </summary>
        /// <param name="sumatoriaImportes">Suma de los importes del ExtractoProveedor</param>
        /// <returns>Saldo anterior en formato Debe - Haber (negativo si hay facturas pendientes)</returns>
        public static decimal ConvertirSaldoAnteriorProveedor(decimal sumatoriaImportes)
        {
            // Para proveedores:
            // - Importe positivo en ExtractoProveedor = Haber (factura de proveedor)
            // - Importe negativo en ExtractoProveedor = Debe (pago a proveedor)
            // El saldo en formato Debe-Haber es el inverso
            return -sumatoriaImportes;
        }

        /// <summary>
        /// Calcula el saldo acumulado para cada movimiento.
        /// El saldo se calcula como: SaldoAnterior + Sum(Debe - Haber)
        /// </summary>
        /// <param name="movimientos">Lista de movimientos a procesar</param>
        /// <param name="saldoAnterior">Saldo anterior (ya convertido a formato Debe-Haber)</param>
        /// <returns>Tupla con (TotalDebe, TotalHaber, SaldoFinal)</returns>
        public static (decimal TotalDebe, decimal TotalHaber, decimal SaldoFinal) CalcularSaldos(
            List<MovimientoMayorDTO> movimientos,
            decimal saldoAnterior)
        {
            decimal saldoAcumulado = saldoAnterior;
            decimal totalDebe = 0;
            decimal totalHaber = 0;

            foreach (var mov in movimientos)
            {
                totalDebe += mov.Debe;
                totalHaber += mov.Haber;
                saldoAcumulado += mov.Debe - mov.Haber;
                mov.Saldo = saldoAcumulado;
            }

            return (totalDebe, totalHaber, saldoAcumulado);
        }

        /// <summary>
        /// Elimina los pares de paso a cartera que no afectan al saldo.
        /// Un paso a cartera consiste en:
        /// - Factura: TipoApunte = 1, Importe positivo
        /// - Paso a cartera: TipoApunte = 0, mismo documento, misma fecha, importe negativo (mismo valor absoluto)
        /// Ambos movimientos se anulan, por lo que se pueden eliminar para simplificar el extracto.
        /// </summary>
        /// <param name="movimientos">Lista de movimientos a procesar</param>
        /// <returns>Lista de movimientos sin los pares de paso a cartera</returns>
        public static List<MovimientoMayorDTO> EliminarPasoACartera(List<MovimientoMayorDTO> movimientos)
        {
            var resultado = new List<MovimientoMayorDTO>();
            var procesados = new HashSet<int>(); // Indices de movimientos ya procesados

            for (int i = 0; i < movimientos.Count; i++)
            {
                if (procesados.Contains(i))
                {
                    continue;
                }

                var mov = movimientos[i];

                // Buscar si es una factura (TipoApunte = 1) que tiene su correspondiente paso a cartera
                if (mov.TipoApunte == "1")
                {
                    // Buscar el paso a cartera correspondiente (TipoApunte = 0)
                    int indicePasoCartera = -1;
                    for (int j = 0; j < movimientos.Count; j++)
                    {
                        if (procesados.Contains(j) || i == j)
                        {
                            continue;
                        }

                        var otro = movimientos[j];
                        if (otro.TipoApunte == "0" &&
                            otro.NumeroDocumento == mov.NumeroDocumento &&
                            otro.Fecha == mov.Fecha &&
                            EsImporteOpuesto(mov, otro))
                        {
                            indicePasoCartera = j;
                            break;
                        }
                    }

                    if (indicePasoCartera >= 0)
                    {
                        // Encontramos el par - eliminamos ambos
                        procesados.Add(i);
                        procesados.Add(indicePasoCartera);
                        continue;
                    }
                }

                // Si llegamos aqui, el movimiento no forma parte de un par a eliminar
                resultado.Add(mov);
            }

            return resultado;
        }

        /// <summary>
        /// Verifica si dos movimientos tienen importes opuestos (mismo valor absoluto, signos contrarios).
        /// Para clientes: factura tiene Debe > 0, paso cartera tiene Haber > 0
        /// Para proveedores: factura tiene Haber > 0, paso cartera tiene Debe > 0
        /// </summary>
        private static bool EsImporteOpuesto(MovimientoMayorDTO factura, MovimientoMayorDTO pasoCartera)
        {
            // Para clientes: Factura tiene Debe, PasoCartera tiene Haber
            if (factura.Debe > 0 && pasoCartera.Haber > 0)
            {
                return factura.Debe == pasoCartera.Haber;
            }

            // Para proveedores: Factura tiene Haber, PasoCartera tiene Debe
            if (factura.Haber > 0 && pasoCartera.Debe > 0)
            {
                return factura.Haber == pasoCartera.Debe;
            }

            return false;
        }
    }
}
