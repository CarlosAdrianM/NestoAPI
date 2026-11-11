using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>
    /// Lo que hay que saber de un envío del reparto propio para decidir si se puede anular (<see cref="AnulacionEnvioRepartoPropio"/>).
    /// Cada tabla que no exista (script sin lanzar) cuenta como «sin datos».
    /// </summary>
    public class DatosAnulacionEnvioRepartoPropio
    {
        /// <summary>Algún bulto suyo en EnviosAgenciaCargaBultos.</summary>
        public bool Cargado { get; set; }
        /// <summary>Algún movimiento suyo en RepartoBultosMovimientos (cargado, recogido, entregado, descargado).</summary>
        public bool MovimientosFurgoneta { get; set; }
        /// <summary>Alguna fila suya en RepartoCobrosTarjeta (cobro con tarjeta o enlace de pago).</summary>
        public bool CobroTarjeta { get; set; }
        /// <summary>Alguna fila suya en RepartoAutorizacionesReembolso.</summary>
        public bool AutorizacionReembolso { get; set; }
        /// <summary>Alguna fila suya en EnviosAgenciaPruebasEntrega (entrega o incidencia).</summary>
        public bool PruebaEntrega { get; set; }
        /// <summary>Su fila en EnviosAgenciaReembolsosAlEntregar.</summary>
        public bool ReembolsoAlEntregar { get; set; }
        /// <summary>Es el origen o el nuevo de un reenvío (EnviosAgenciaReenvios).</summary>
        public bool Reenvio { get; set; }
        /// <summary>Si el pedido es una nota (CabPedidoVta.NotaEntrega), su tipo (Entrega, Recogida, Cobro); null si no es una nota.</summary>
        public string TipoNota { get; set; }
    }

    /// <summary>Lee <see cref="DatosAnulacionEnvioRepartoPropio"/>. Sustituible en los tests.</summary>
    public interface IDatosAnulacionEnvioRepartoPropio
    {
        Task<DatosAnulacionEnvioRepartoPropio> Leer(EnviosAgencia envio);
    }

    /// <summary>
    /// Novedades 1162 (Alfredo, 10/11/26, pedido 930294): una etiqueta del reparto propio se registró con un reembolso de 53 € y,
    /// como en la 14 imprimir la etiqueta es tramitar, quedó Tramitada (envío 250772). No se podía ni modificar ni anular (Anular
    /// solo admitía En curso) y acabó habiendo otro envío para el mismo pedido y dos paradas para el repartidor. Ahora, en la 14,
    /// Anular admite también el Tramitado mientras no haya empezado a repartirse: vuelve a etiqueta pendiente para corregirlo en
    /// Pendientes y volver a imprimirlo (el albarán es RP{Numero}, así que conserva su código), o borrarlo.
    /// </summary>
    public static class AnulacionEnvioRepartoPropio
    {
        public const string CAMPO_HISTORIA = "Estado";
        public const string OBSERVACIONES_HISTORIA = "Anulado el envío tramitado del reparto propio: vuelve a etiqueta pendiente";

        /// <summary>¿Lo decide esta regla? Solo la agencia 14 (las demás agencias, como siempre).</summary>
        public static bool Aplica(EnviosAgencia envio) => envio != null && envio.Agencia == Constantes.Agencias.AGENCIA_REPARTO_PROPIO;

        /// <summary>
        /// Null si el envío (del reparto propio, En curso o Tramitado) se puede anular; si no, el motivo para el usuario (409).
        /// Las etiquetas pendientes no pasan por aquí: se borran directamente.
        /// </summary>
        public static string Validar(EnviosAgencia envio, DatosAnulacionEnvioRepartoPropio datos)
        {
            if (envio == null)
            {
                throw new ArgumentNullException(nameof(envio));
            }
            datos = datos ?? new DatosAnulacionEnvioRepartoPropio();
            int numero = envio.Numero;

            if (envio.Estado > Constantes.Agencias.ESTADO_TRAMITADO)
            {
                return MotivoPorEstado(numero, envio.Estado);
            }
            // Las paradas de las notas nacen tramitadas con su propio ciclo (Facturar rutas, «Nueva nota de cobro»).
            if (!string.IsNullOrWhiteSpace(datos.TipoNota))
            {
                return $"El envío {numero} es la parada de la nota de {TextoTipoNota(datos.TipoNota)} del pedido {envio.Pedido}: " +
                    "no se anula desde aquí, porque esas paradas siguen su propio camino.";
            }
            if (datos.Cargado)
            {
                return $"El envío {numero} ya está cargado en la furgoneta: no se puede anular. " +
                    "Si al final no se entrega, el repartidor lo marcará como incidencia.";
            }
            if (datos.MovimientosFurgoneta)
            {
                return $"El envío {numero} ya ha pasado por la furgoneta (cargado, recogido o descargado): no se puede anular.";
            }
            if (datos.PruebaEntrega)
            {
                return $"El envío {numero} ya tiene una entrega o una incidencia del repartidor: no se puede anular.";
            }
            if (datos.CobroTarjeta)
            {
                return $"El envío {numero} tiene un cobro con tarjeta o un enlace de pago: no se puede anular.";
            }
            if (datos.AutorizacionReembolso)
            {
                return $"El envío {numero} tiene una autorización de reembolso del repartidor: no se puede anular.";
            }
            if (datos.ReembolsoAlEntregar)
            {
                return $"El envío {numero} tiene apuntado el cobro del reembolso al entregar: no se puede anular.";
            }
            if (datos.Reenvio)
            {
                return $"El envío {numero} tiene un reenvío: no se puede anular.";
            }
            return null;
        }

        /// <summary>
        /// Lo deja como etiqueta pendiente. FechaPagoReembolso a null: en la 14 nace puesta con el reembolso (#778) y, si no se
        /// quita, se quedaría con la fecha y el importe de antes aunque en Pendientes se cambie el reembolso; al registrarlo de nuevo
        /// <see cref="ReembolsoRepartoPropio.AplicarFechaPago"/> la vuelve a poner si lleva reembolso.
        /// </summary>
        /// <returns>La fila de la historia (Campo «Estado», ValorAnterior el estado de antes).</returns>
        public static EnvioHistoria Aplicar(EnviosAgencia envio, string usuario, DateTime ahora)
        {
            if (envio == null)
            {
                throw new ArgumentNullException(nameof(envio));
            }
            short anterior = envio.Estado;
            envio.CodigoBarras = string.Empty;
            envio.Estado = (short)Constantes.Agencias.ESTADO_PENDIENTE;
            envio.FechaPagoReembolso = null;
            string quien = string.IsNullOrWhiteSpace(usuario) ? "NestoAPI" : usuario.Trim();
            return new EnvioHistoria
            {
                NumeroEnvio = envio.Numero,
                Campo = CAMPO_HISTORIA,
                ValorAnterior = anterior.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Observaciones = OBSERVACIONES_HISTORIA,
                Usuario = quien.Length <= 50 ? quien : quien.Substring(0, 50),
                FechaModificacion = ahora
            };
        }

        private static string MotivoPorEstado(int numero, short estado)
        {
            switch (estado)
            {
                case Constantes.Agencias.ESTADO_ENTREGADO:
                    return $"El envío {numero} ya está entregado: no se puede anular.";
                case Constantes.Agencias.ESTADO_INCIDENTADO:
                    return $"El envío {numero} tiene una incidencia del reparto: no se puede anular. Resuélvala desde la incidencia (reenvío o devolución).";
                case Constantes.Agencias.ESTADO_DEVUELTO:
                    return $"El envío {numero} ya está devuelto: no se puede anular.";
                case Constantes.Agencias.ESTADO_CERRADO_A_MANO:
                    return $"El envío {numero} ya está cerrado: no se puede anular.";
                default:
                    return $"El envío {numero} ya no está tramitado (estado {estado}): no se puede anular.";
            }
        }

        private static string TextoTipoNota(string tipo)
        {
            switch (TiposNotaEntrega.Normalizar(tipo))
            {
                case TiposNotaEntrega.RECOGIDA:
                    return "recogida";
                case TiposNotaEntrega.COBRO:
                    return "cobro";
                default:
                    return "entrega";
            }
        }
    }

    /// <summary>
    /// NestoAPI#847 (11/11/26): la parada de una nota de RECOGIDA o de COBRO no lleva etiqueta RP: no sale ningún bulto (los de una
    /// recogida son NR{pedido}-{N} y los da la nota). Reimprimirla acabó en un bulto RP fantasma en la furgoneta.
    /// </summary>
    public static class EtiquetaRepartoPropioDeNota
    {
        /// <summary>El motivo (de usted) por el que el envío no lleva etiqueta RP, o null si la lleva.</summary>
        public static string MotivoSinEtiqueta(EnviosAgencia envio, string tipoNota)
        {
            if (envio == null || envio.Agencia != Constantes.Agencias.AGENCIA_REPARTO_PROPIO)
            {
                return null;
            }
            switch (TiposNotaEntrega.Normalizar(tipoNota))
            {
                case TiposNotaEntrega.RECOGIDA:
                    return $"El envío {envio.Numero} es la parada de la nota de recogida {envio.Pedido}: no lleva etiqueta RP, porque no sale " +
                        $"ningún bulto. Las etiquetas de lo que se recoge (NR{envio.Pedido}-1, NR{envio.Pedido}-2…) se imprimen desde la nota de recogida.";
                case TiposNotaEntrega.COBRO:
                    return $"El envío {envio.Numero} es la parada de la nota de cobro {envio.Pedido}: no lleva bultos ni etiqueta.";
                default:
                    return null;
            }
        }
    }

    /// <summary>
    /// Las tablas del reparto van por SQL, fuera del EDMX (como sus repositorios): cada una se mira solo si existe. La nota, por EF
    /// (CabPedidoVta.NotaEntrega) y su tipo de NotasEntregaCabecera si está la tabla (sin ella, «Entrega»).
    /// </summary>
    public class DatosAnulacionEnvioRepartoPropioEF : IDatosAnulacionEnvioRepartoPropio
    {
        private readonly NVEntities db;

        public DatosAnulacionEnvioRepartoPropioEF(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<DatosAnulacionEnvioRepartoPropio> Leer(EnviosAgencia envio)
        {
            if (envio == null)
            {
                throw new ArgumentNullException(nameof(envio));
            }
            int numero = envio.Numero;
            var datos = new DatosAnulacionEnvioRepartoPropio
            {
                Cargado = await Hay(RepositorioCargaFurgoneta.TABLA, "EnvioId = @p0", numero).ConfigureAwait(false),
                MovimientosFurgoneta = await Hay(RepositorioFurgoneta.TABLA, "EnvioId = @p0", numero).ConfigureAwait(false),
                CobroTarjeta = await Hay(RepositorioCobrosTarjetaReparto.TABLA, "EnvioId = @p0", numero).ConfigureAwait(false),
                AutorizacionReembolso = await Hay(RepositorioAutorizacionesReembolso.TABLA, "EnvioId = @p0", numero).ConfigureAwait(false),
                PruebaEntrega = await Hay(RepositorioRepartoPropio.TABLA, "EnvioId = @p0", numero).ConfigureAwait(false),
                ReembolsoAlEntregar = await Hay(RegistroReembolsosAlEntregar.TABLA, "EnvioId = @p0", numero).ConfigureAwait(false),
                Reenvio = await Hay(RepositorioReenviosEnvio.TABLA, "EnvioOrigen = @p0 OR EnvioNuevo = @p0", numero).ConfigureAwait(false)
            };
            if (envio.Pedido.HasValue && envio.Pedido.Value > 0)
            {
                datos.TipoNota = await TipoNota(envio.Pedido.Value).ConfigureAwait(false);
            }
            return datos;
        }

        private async Task<bool> Hay(string tabla, string condicion, int numero)
        {
            if (!ColumnasOpcionales.ExisteTabla(db, tabla))
            {
                return false;
            }
            List<int> filas = await db.Database.SqlQuery<int>($"SELECT TOP 1 1 FROM dbo.{tabla} WHERE {condicion}", numero)
                .ToListAsync().ConfigureAwait(false);
            return filas.Count > 0;
        }

        // Como LeerEnviosDelPedido de las paradas: el envío se busca por el número de pedido.
        /// <summary>NestoAPI#847: también para no imprimir la etiqueta RP de una recogida o un cobro.</summary>
        public async Task<string> TipoNota(int pedido)
        {
            List<string> empresas = await db.CabPedidoVtas.AsNoTracking()
                .Where(c => c.Número == pedido && c.NotaEntrega)
                .Select(c => c.Empresa)
                .ToListAsync().ConfigureAwait(false);
            if (empresas.Count == 0)
            {
                return null;
            }
            FilaNotaEntregaCabecera fila = await new RepositorioNotasEntregaCabecera(db).Leer(empresas[0]?.Trim(), pedido).ConfigureAwait(false);
            return TiposNotaEntrega.Normalizar(fila?.Tipo) ?? TiposNotaEntrega.ENTREGA;
        }
    }
}
