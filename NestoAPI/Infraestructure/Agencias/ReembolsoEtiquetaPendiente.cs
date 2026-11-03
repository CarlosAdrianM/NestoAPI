using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.Agencias
{
    /// <summary>NestoAPI#785: qué se hace con el reembolso de una etiqueta pendiente al registrarla (imprimirla).</summary>
    public enum DecisionReembolsoPendiente
    {
        /// <summary>Se queda como está (ya tenía importe, era «no cobrar», o el pedido no tiene nada que cobrar).</summary>
        SinCambios,
        /// <summary>Era 0 y el pedido ya tiene picking: se pone lo calculado (y queda en la historia del envío).</summary>
        Recalculado,
        /// <summary>Era 0 porque alguien lo cambió a mano: se respeta y solo se avisa.</summary>
        RespetadoCambioManual,
        /// <summary>Contra reembolso, la etiqueta sigue a 0 € y la factura tiene importe pendiente: no se imprime.</summary>
        Bloqueado
    }

    public class ResultadoReembolsoPendiente
    {
        public DecisionReembolsoPendiente Decision { get; set; }
        /// <summary>El reembolso con el que se registra la etiqueta (con <see cref="DecisionReembolsoPendiente.Bloqueado"/>, no se registra).</summary>
        public decimal Reembolso { get; set; }
        /// <summary>El texto para el usuario (avisos de la respuesta / 409); null con SinCambios.</summary>
        public string Aviso { get; set; }
    }

    /// <summary>
    /// NestoAPI#785 (pedido 929609, envío 250432, 30/10/26): una etiqueta pendiente creada antes de que el pedido tuviera picking
    /// guarda reembolso 0 (<see cref="GestorEnviosAgencia.ImporteReembolso(CabPedidoVta, IEnumerable{LinPedidoVta}, IServicioPedidosVenta)"/>
    /// suma solo las líneas con picking) y al registrarla se reutilizaba tal cual: la agencia fue sin reembolso y hubo que volver a por
    /// 134,16 €. El núcleo común a los dos caminos que registran un pendiente: la propuesta de envío (Ariadna y Agencias de Nesto
    /// por la API, <see cref="PropuestaEnvioService"/> + <see cref="ServicioEnvioDesdePedido"/>) y el PUT de Agencias de Nesto
    /// (InsertarRegistro, <c>EnviosAgenciasController.PutEnviosAgencia</c>). «Tramitar todos» no registra pendientes (los rechaza).
    /// </summary>
    public static class ReembolsoEtiquetaPendiente
    {
        public const string CAMPO_HISTORIA = "Reembolso";
        public const string OBSERVACIONES_RECALCULADO = "Recalculado al registrar la etiqueta pendiente: el pedido ya tiene picking";
        public const string OBSERVACIONES_CAMBIO_MANUAL = "NestoAPI#785: reembolso cambiado a mano en la etiqueta pendiente";
        public const string MENSAJE_BLOQUEO = "El pedido es contra reembolso y la etiqueta lleva 0 €";
        /// <summary>Código del 409 del PUT y del aviso bloqueante de la propuesta.</summary>
        public const string CODIGO_REEMBOLSO_CERO = "ReembolsoCeroContraReembolso";

        private const string USUARIO_SISTEMA = "NestoAPI";
        private static readonly CultureInfo Espanol = new CultureInfo("es-ES");

        /// <param name="numeroEnvio">El envío pendiente (para los textos).</param>
        /// <param name="guardado">El Reembolso del pendiente en la BD (−1 = «no cobrar», <see cref="Constantes.Agencias.REEMBOLSO_NO_COBRAR"/>).</param>
        /// <param name="calculado">El reembolso de hoy: <see cref="GestorEnviosAgencia.CalcularReembolso"/> sobre las líneas con picking.</param>
        /// <param name="cambiadoAMano">Hay constancia de que alguien cambió a mano el reembolso del pendiente (<see cref="HayCambioManual"/>).</param>
        /// <param name="facturadoSinCobrar">Lo que la factura del pedido contra reembolso tiene pendiente de cobrar en efectivo
        /// (<see cref="GestorEnviosAgencia.FacturadoContraReembolsoSinCobrar"/>); 0 si no es contra reembolso o no está facturado.</param>
        public static ResultadoReembolsoPendiente Decidir(int numeroEnvio, decimal guardado, decimal calculado, bool cambiadoAMano, decimal facturadoSinCobrar)
        {
            // Ya trae importe (tienda online, NestoApp, el de la creación) o se creó como «no cobrar»: manda lo que tiene.
            if (guardado != 0m)
            {
                return SinCambios(guardado);
            }

            if (cambiadoAMano)
            {
                if (calculado <= 0m && facturadoSinCobrar <= 0m)
                {
                    return SinCambios(0m);
                }
                decimal segun = calculado > 0m ? calculado : facturadoSinCobrar;
                return new ResultadoReembolsoPendiente
                {
                    Decision = DecisionReembolsoPendiente.RespetadoCambioManual,
                    Reembolso = 0m,
                    Aviso = string.Format(Espanol, "La etiqueta pendiente {0} sale con reembolso 0,00 € porque se cambió a mano, aunque el pedido " +
                        "tiene {1:N2} € que cobrar. Se respeta: compruebe que es correcto.", numeroEnvio, segun)
                };
            }

            if (calculado > 0m)
            {
                return new ResultadoReembolsoPendiente
                {
                    Decision = DecisionReembolsoPendiente.Recalculado,
                    Reembolso = calculado,
                    Aviso = string.Format(Espanol, "La etiqueta pendiente {0} se creó con reembolso 0,00 € antes de que el pedido tuviera picking: " +
                        "se ha recalculado a {1:N2} €.", numeroEnvio, calculado)
                };
            }

            if (facturadoSinCobrar > 0m)
            {
                return new ResultadoReembolsoPendiente
                {
                    Decision = DecisionReembolsoPendiente.Bloqueado,
                    Reembolso = 0m,
                    Aviso = MensajeBloqueo(numeroEnvio, facturadoSinCobrar)
                };
            }

            return SinCambios(0m);
        }

        internal static string MensajeBloqueo(int numeroEnvio, decimal facturadoSinCobrar)
            => string.Format(Espanol, "{0} (envío {1}): la factura tiene {2:N2} € pendientes de cobro. Ponga el reembolso en la etiqueta " +
                "pendiente antes de imprimirla.", MENSAJE_BLOQUEO, numeroEnvio, facturadoSinCobrar);

        private static ResultadoReembolsoPendiente SinCambios(decimal guardado)
            => new ResultadoReembolsoPendiente { Decision = DecisionReembolsoPendiente.SinCambios, Reembolso = guardado < 0m ? 0m : guardado };

        /// <summary>
        /// ¿Alguien cambió a mano el reembolso del pendiente? Hay una fila de su historia del campo Reembolso de un usuario de verdad
        /// (no «NestoAPI») que no es la de este recálculo. Las deja el PUT cuando se cambia el reembolso de un pendiente
        /// (<see cref="FilaCambioManual"/>).
        /// </summary>
        public static bool HayCambioManual(IEnumerable<EnvioHistoria> historia)
            => (historia ?? Enumerable.Empty<EnvioHistoria>()).Any(h =>
                string.Equals(h.Campo?.Trim(), CAMPO_HISTORIA, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(h.Usuario)
                && !string.Equals(h.Usuario.Trim(), USUARIO_SISTEMA, StringComparison.OrdinalIgnoreCase)
                && (h.Observaciones == null || !h.Observaciones.StartsWith(OBSERVACIONES_RECALCULADO, StringComparison.Ordinal)));

        /// <summary>La fila de la historia del recálculo (ValorAnterior «0.00»).</summary>
        public static EnvioHistoria FilaRecalculado(int numeroEnvio, decimal anterior, string usuario, DateTime ahora)
            => Fila(numeroEnvio, anterior, OBSERVACIONES_RECALCULADO, usuario, ahora);

        /// <summary>La marca de que el reembolso del pendiente se ha cambiado a mano (la respeta el registro).</summary>
        public static EnvioHistoria FilaCambioManual(int numeroEnvio, decimal anterior, decimal nuevo, string usuario, DateTime ahora)
            => Fila(numeroEnvio, anterior, string.Format(Espanol, "{0} ({1:N2} € → {2:N2} €).", OBSERVACIONES_CAMBIO_MANUAL, anterior, nuevo), usuario, ahora);

        private static EnvioHistoria Fila(int numeroEnvio, decimal anterior, string observaciones, string usuario, DateTime ahora)
        {
            string quien = string.IsNullOrWhiteSpace(usuario) ? USUARIO_SISTEMA : usuario.Trim();
            return new EnvioHistoria
            {
                NumeroEnvio = numeroEnvio,
                Campo = CAMPO_HISTORIA,
                ValorAnterior = anterior.ToString("0.00", CultureInfo.InvariantCulture),
                Observaciones = observaciones,
                Usuario = quien.Length > 50 ? quien.Substring(0, 50) : quien,
                FechaModificacion = ahora
            };
        }
    }

    /// <summary>NestoAPI#785: lo que necesita el registro de un pendiente para decidir su reembolso (doble en los tests).</summary>
    public interface IDatosReembolsoEtiquetaPendiente
    {
        /// <summary>El reembolso de hoy con las líneas con picking (el de GET PedidosVenta/ImporteReembolso).</summary>
        decimal ReembolsoCalculado(string empresa, int pedido);
        /// <summary><see cref="GestorEnviosAgencia.FacturadoContraReembolsoSinCobrar"/>.</summary>
        decimal FacturadoContraReembolsoSinCobrar(string empresa, int pedido);
        /// <summary><see cref="ReembolsoEtiquetaPendiente.HayCambioManual"/> sobre la historia del envío.</summary>
        bool ReembolsoCambiadoAMano(int numeroEnvio);
    }

    public class DatosReembolsoEtiquetaPendienteEF : IDatosReembolsoEtiquetaPendiente
    {
        private readonly NVEntities db;
        private readonly Func<IServicioPedidosVenta> servicioPedidosVenta;

        public DatosReembolsoEtiquetaPendienteEF(NVEntities db, Func<IServicioPedidosVenta> servicioPedidosVenta)
        {
            this.db = db;
            this.servicioPedidosVenta = servicioPedidosVenta;
        }

        public decimal ReembolsoCalculado(string empresa, int pedido)
            => new GestorPedidosVenta(servicioPedidosVenta()).ImporteReembolso(empresa, pedido);

        public decimal FacturadoContraReembolsoSinCobrar(string empresa, int pedido)
            => new GestorPedidosVenta(servicioPedidosVenta()).FacturadoContraReembolsoSinCobrar(empresa, pedido);

        public bool ReembolsoCambiadoAMano(int numeroEnvio)
            => ReembolsoEtiquetaPendiente.HayCambioManual(db.EnviosHistorias.AsNoTracking()
                .Where(h => h.NumeroEnvio == numeroEnvio && h.Campo == ReembolsoEtiquetaPendiente.CAMPO_HISTORIA)
                .ToList());
    }
}
