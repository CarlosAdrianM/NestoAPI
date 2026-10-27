using System;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Abandono
{
    /// <summary>Cómo acabó una fila de ClientesRiesgoAbandono al vencer su horizonte.</summary>
    public static class ResultadoAbandono
    {
        /// <summary>Volvió a pedir dentro del horizonte (y no se dio de baja): la regla se equivocó si lo había marcado.</summary>
        public const string COMPRO = "Compro";
        /// <summary>Ni pidió ni cambió de estado en todo el horizonte.</summary>
        public const string ABANDONO = "Abandono";
        /// <summary>Alguien lo pasó a un estado de baja (<see cref="ResolucionAbandono.ESTADOS_BAJA"/>) dentro del horizonte; manda sobre Compró.</summary>
        public const string BAJA = "Baja";
        /// <summary>La ficha ya no existe (o no se pudo leer): no se puede saber.</summary>
        public const string DESCONOCIDO = "Desconocido";
    }

    /// <summary>
    /// NestoAPI#714: la etiqueta de una fila cuando vence su horizonte, como función pura. Positivo (la regla acertó) = Abandono o
    /// Baja; negativo = Compró; Desconocido no cuenta para la precisión. Los hechos (si compró y cuándo, si pasó a baja y cuándo)
    /// los lee <see cref="RepositorioRiesgoAbandonoSql"/> en (d, FechaLimite]: las compras de CabPedidoVta/LinPedidoVta y los
    /// cambios de estado de SeguimientoCambiosEstadoCliente (desde 01/2025 tiene todos; LogModificaciones queda para
    /// reconstruir el histórico en el corte 2).
    /// </summary>
    public static class ResolucionAbandono
    {
        /// <summary>Estados de cliente que la etiqueta considera baja: 9, 99, -1 (anulado), 11, 22, 93 (sin acción comercial) y 95 (una sola compra Amazon).</summary>
        public static readonly IReadOnlyCollection<int> ESTADOS_BAJA = new[] { 9, 99, -1, 11, 22, 93, 95 };
        /// <summary>Los mismos, para concatenar en SQL.</summary>
        public const string ESTADOS_BAJA_SQL = "9, 99, -1, 11, 22, 93, 95";

        public static bool EsEstadoBaja(int estado) => ((ICollection<int>)ESTADOS_BAJA).Contains(estado);

        /// <summary>
        /// Decide el resultado. <paramref name="fechaBaja"/>: primer cambio a un estado de baja dentro del horizonte;
        /// <paramref name="fechaPrimeraCompra"/>: primer pedido dentro del horizonte. Devuelve el resultado y la fecha del hecho
        /// que lo decide (null en Abandono y Desconocido).
        /// </summary>
        public static (string resultado, DateTime? fechaEvento) Decidir(bool clienteExiste, DateTime? fechaBaja, DateTime? fechaPrimeraCompra)
        {
            if (!clienteExiste)
            {
                return (ResultadoAbandono.DESCONOCIDO, null);
            }
            if (fechaBaja.HasValue)
            {
                return (ResultadoAbandono.BAJA, fechaBaja.Value.Date);
            }
            if (fechaPrimeraCompra.HasValue)
            {
                return (ResultadoAbandono.COMPRO, fechaPrimeraCompra.Value.Date);
            }
            return (ResultadoAbandono.ABANDONO, null);
        }

        /// <summary>Abandono y Baja son el positivo de la etiqueta (la regla acertó al marcarlo).</summary>
        public static bool EsPositivo(string resultado) => resultado == ResultadoAbandono.ABANDONO || resultado == ResultadoAbandono.BAJA;
    }

    /// <summary>Una fila de ClientesRiesgoAbandono pendiente de resolver, con los hechos leídos para decidirla.</summary>
    public class PendienteResolverAbandono
    {
        public int Id { get; set; }
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public DateTime FechaInstantanea { get; set; }
        public DateTime FechaLimite { get; set; }
        public bool ClienteExiste { get; set; }
        public DateTime? FechaBaja { get; set; }
        public DateTime? FechaPrimeraCompra { get; set; }
    }
}
