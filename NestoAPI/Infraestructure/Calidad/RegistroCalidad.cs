using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using System.Web.Hosting;

namespace NestoAPI.Infraestructure.Calidad
{
    /// <summary>
    /// NestoAPI#914: los tipos de dbo.CalidadEventos. Cada uno, dónde se apunta y qué motivo del pedido perfecto alimenta, en
    /// docs/claude/calidad-kpi.md. Un tipo nuevo = una constante aquí + su línea en el documento.
    /// </summary>
    public static class TiposEventoCalidad
    {
        /// <summary>Alguien pidió a Almacén cambiar el modo de entrega de un pedido que ya tenía picking (correo de #533).</summary>
        public const string CAMBIO_DE_MODO_CON_PICKING = "CambioDeModoConPicking";
        /// <summary>Se quitó el picking de un pedido (entero o unas líneas) con «Quitar picking» de Nesto.</summary>
        public const string QUITAR_PICKING = "QuitarPicking";
        /// <summary>Se añadieron líneas a un pedido en preparación y se fueron a un pedido nuevo (PedidoNuevo).</summary>
        public const string AMPLIACION_CON_PICKING_PEDIDO_NUEVO = "AmpliacionConPickingPedidoNuevo";
        /// <summary>Se añadieron líneas a un pedido en preparación y se quedaron en el mismo (PedirAlmacen o aviso a Almacén).</summary>
        public const string AMPLIACION_CON_PICKING_MISMO_PEDIDO = "AmpliacionConPickingMismoPedido";
        /// <summary>Se anuló la parada de una recogida del reparto propio.</summary>
        public const string RECOGIDA_ANULADA = "RecogidaAnulada";
        /// <summary>Se anuló una recogida que el repartidor había marcado «Recogido» por error (caso 930553).</summary>
        public const string RECOGIDA_MARCADA_POR_ERROR = "RecogidaMarcadaPorError";
        /// <summary>Envío de la ruta propia metido a mano fuera de su día de reparto. SIN CONECTAR: no hay un punto único donde se fuerce.</summary>
        public const string ENVIO_RUTA_PROPIA_FUERA_DE_DIA = "EnvioRutaPropiaFueraDeDia";
        /// <summary>El repartidor confirmó (tras el 409 COBRO_DE_MAS) cobrar en efectivo más que el reembolso y la entrega se registró.</summary>
        public const string COBRO_DE_MAS_CONFIRMADO = "CobroDeMasConfirmado";
        /// <summary>El repartidor confirmó (tras el 409 COBRO_REPETIDO o COBRO_TODO_A_CUENTA) un cobro de deuda y se contabilizó.</summary>
        public const string COBRO_REPETIDO_CONFIRMADO = "CobroRepetidoConfirmado";
        /// <summary>
        /// NestoAPI#914 corte 2: en el recorrido (picking o reposición) el producto no estaba en el hueco de la parada y el mozo lo cogió de otro
        /// (Ariadna#12, «Cambiar hueco»). Lo que no estaba pasa a «pendiente de ubicar» y hasta ahora no quedaba constancia de quién, qué y dónde.
        /// </summary>
        public const string NO_ESTABA_EN_SU_HUECO = "NoEstabaEnSuHueco";
        /// <summary>
        /// NestoAPI#914 (Carlos 27/11/26, «avisar y dejar seguir»): se grabó el envío de la etiqueta del packing con productos sin leer en la mesa
        /// (ServicioEtiquetaPacking.Imprimir). Detalle {Modo «Confirmado» o «SinAviso», Picking, Unidades, Productos}. Indicador del EMPAQUETADOR.
        /// </summary>
        public const string ETIQUETA_CON_PACKING_INCOMPLETO = "EtiquetaConPackingIncompleto";

        /// <summary>Los que dicen que el pedido se tocó después del picking (motivo TocadoTrasElPicking).</summary>
        public static readonly IReadOnlyList<string> TOCADO_TRAS_EL_PICKING = new[] { QUITAR_PICKING, AMPLIACION_CON_PICKING_MISMO_PEDIDO };
        /// <summary>Los que dicen que se pidió cambiar algo con el picking ya sacado (motivo PidioCambiarAlgoConPicking).</summary>
        public static readonly IReadOnlyList<string> PIDIO_CAMBIAR_CON_PICKING = new[] { CAMBIO_DE_MODO_CON_PICKING, AMPLIACION_CON_PICKING_PEDIDO_NUEVO };
        /// <summary>Motivo EnvioDeRutaPropiaForzado.</summary>
        public static readonly IReadOnlyList<string> RUTA_PROPIA_FORZADA = new[] { ENVIO_RUTA_PROPIA_FUERA_DE_DIA };
    }

    /// <summary>Una fila de dbo.CalidadEventos.</summary>
    public class EventoCalidad
    {
        public DateTime Fecha { get; set; }
        public string Tipo { get; set; }
        public string Empresa { get; set; }
        public int? Pedido { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Documento { get; set; }
        public string Usuario { get; set; }
        public string Imputado { get; set; }
        public string Detalle { get; set; }
        public decimal? Importe { get; set; }
    }

    public interface IEscritorEventosCalidad
    {
        void Escribir(EventoCalidad evento);
    }

    /// <summary>
    /// NestoAPI#914: EL ÚNICO punto para apuntar una señal de calidad (algo que se sale del pedido perfecto y hoy solo salía por correo o
    /// push). Garantías: nunca lanza, nunca retrasa la operación (la escritura va en segundo plano, con su propia conexión, sin alistarse
    /// en ninguna transacción de quien llama), y sin la tabla (Scripts/Issue914_CalidadEventos.sql) no hace nada: ELMAH una sola vez.
    /// </summary>
    public static class RegistroCalidad
    {
        public const int LONGITUD_DETALLE = 1000;
        internal const int ERROR_OBJETO_NO_EXISTE = 208;

        /// <summary>Para los tests: dónde se escribe.</summary>
        internal static Func<IEscritorEventosCalidad> Escritor { get; set; } = () => EscritorEventosCalidadSql.DesdeConfiguracion();
        /// <summary>Para los tests: cómo se lanza en segundo plano (en los tests, en el acto).</summary>
        internal static Action<Action> EnSegundoPlano { get; set; } = LanzarEnSegundoPlano;
        internal static Action<Exception> Registrador { get; set; } = ex => ElmahHelper.Log(ex, "Sistema (registro de calidad)");
        internal static Func<DateTime> Ahora { get; set; } = () => DateTime.Now;

        private static int avisadoSinTabla;

        /// <param name="tipo">Una constante de <see cref="TiposEventoCalidad"/>.</param>
        /// <param name="usuario">Quien lo provoca (se guarda sin dominio).</param>
        /// <param name="imputado">A quién se imputa, solo si se sabe en el momento.</param>
        /// <param name="detalle">Un objeto pequeño («de qué a qué»): se guarda en JSON, como mucho 1.000 caracteres.</param>
        /// <param name="importe">Solo un coste que salga solo (nunca tecleado).</param>
        public static void Apuntar(string tipo, string usuario, string empresa = null, int? pedido = null, string cliente = null,
            string contacto = null, string documento = null, string imputado = null, object detalle = null, decimal? importe = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(tipo))
                {
                    return;
                }
                EventoCalidad evento = Construir(tipo, usuario, empresa, pedido, cliente, contacto, documento, imputado, detalle, importe, Ahora());
                EnSegundoPlano(() => Escribir(evento));
            }
            catch (Exception ex)
            {
                Avisar(new Exception($"[Calidad #914] No se ha podido apuntar el evento {tipo}: {ex.Message}", ex));
            }
        }

        internal static EventoCalidad Construir(string tipo, string usuario, string empresa, int? pedido, string cliente, string contacto,
            string documento, string imputado, object detalle, decimal? importe, DateTime fecha)
        {
            string json = null;
            if (detalle != null)
            {
                json = detalle as string ?? JsonConvert.SerializeObject(detalle, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            }
            return new EventoCalidad
            {
                Fecha = fecha,
                Tipo = Recortar(tipo, 40),
                Empresa = Recortar(empresa, 3),
                Pedido = pedido,
                Cliente = Recortar(cliente, 10),
                Contacto = Recortar(contacto, 3),
                Documento = Recortar(documento, 30),
                Usuario = Recortar(UsuarioAuditoriaHelper.SinDominio(usuario), 50) ?? "(desconocido)",
                Imputado = Recortar(UsuarioAuditoriaHelper.SinDominio(imputado), 50),
                Detalle = Recortar(json, LONGITUD_DETALLE),
                Importe = importe
            };
        }

        internal static void Escribir(EventoCalidad evento)
        {
            try
            {
                using (new TransactionScope(TransactionScopeOption.Suppress))
                {
                    Escritor()?.Escribir(evento);
                }
            }
            catch (SqlException ex) when (ex.Number == ERROR_OBJETO_NO_EXISTE)
            {
                if (Interlocked.Exchange(ref avisadoSinTabla, 1) == 0)
                {
                    Avisar(new Exception("[Calidad #914] Falta la tabla dbo.CalidadEventos (Scripts/Issue914_CalidadEventos.sql): no se apuntan las señales de calidad. " +
                        "Este aviso sale una sola vez.", ex));
                }
            }
            catch (Exception ex)
            {
                Avisar(new Exception($"[Calidad #914] No se ha podido guardar el evento {evento?.Tipo} (pedido {evento?.Pedido}): {ex.Message}", ex));
            }
        }

        /// <summary>Para los tests: vuelve a avisar de la tabla que falta.</summary>
        internal static void Reiniciar() => Interlocked.Exchange(ref avisadoSinTabla, 0);

        private static void Avisar(Exception ex)
        {
            try
            {
                Registrador?.Invoke(ex);
            }
            catch
            {
                // Ni el aviso puede romper nada
            }
        }

        private static string Recortar(string texto, int largo)
        {
            string limpio = texto?.Trim();
            if (string.IsNullOrEmpty(limpio))
            {
                return null;
            }
            return limpio.Length <= largo ? limpio : limpio.Substring(0, largo);
        }

        private static void LanzarEnSegundoPlano(Action trabajo)
        {
            if (HostingEnvironment.IsHosted)
            {
                HostingEnvironment.QueueBackgroundWorkItem(_ => trabajo());
            }
            else
            {
                _ = Task.Run(trabajo);
            }
        }
    }

    /// <summary>INSERT en dbo.CalidadEventos con su propia conexión (NestoConnection, sin alistar).</summary>
    public class EscritorEventosCalidadSql : IEscritorEventosCalidad
    {
        internal const string NOMBRE_APLICACION = "NestoAPI (calidad #914)";
        internal const string SQL_INSERTAR = @"
INSERT INTO dbo.CalidadEventos (Fecha, Tipo, Empresa, Pedido, Cliente, Contacto, Documento, Usuario, Imputado, Detalle, Importe)
VALUES (@fecha, @tipo, @empresa, @pedido, @cliente, @contacto, @documento, @usuario, @imputado, @detalle, @importe)";

        private readonly string cadenaConexion;

        public EscritorEventosCalidadSql(string cadenaConexion)
        {
            this.cadenaConexion = cadenaConexion;
        }

        /// <summary>Null si no hay cadena de conexión (p. ej. en los tests): no se escribe nada.</summary>
        public static EscritorEventosCalidadSql DesdeConfiguracion()
        {
            ConnectionStringSettings origen = ConfigurationManager.ConnectionStrings["NestoConnection"];
            if (origen == null || string.IsNullOrWhiteSpace(origen.ConnectionString))
            {
                return null;
            }
            return new EscritorEventosCalidadSql(new SqlConnectionStringBuilder(origen.ConnectionString)
            {
                ApplicationName = NOMBRE_APLICACION,
                Enlist = false
            }.ConnectionString);
        }

        public void Escribir(EventoCalidad e)
        {
            using (var conexion = new SqlConnection(cadenaConexion))
            using (var comando = new SqlCommand(SQL_INSERTAR, conexion) { CommandTimeout = 15 })
            {
                comando.Parameters.Add("@fecha", SqlDbType.DateTime).Value = e.Fecha;
                comando.Parameters.Add("@tipo", SqlDbType.VarChar, 40).Value = e.Tipo;
                comando.Parameters.Add("@empresa", SqlDbType.Char, 3).Value = (object)e.Empresa ?? DBNull.Value;
                comando.Parameters.Add("@pedido", SqlDbType.Int).Value = (object)e.Pedido ?? DBNull.Value;
                comando.Parameters.Add("@cliente", SqlDbType.VarChar, 10).Value = (object)e.Cliente ?? DBNull.Value;
                comando.Parameters.Add("@contacto", SqlDbType.VarChar, 3).Value = (object)e.Contacto ?? DBNull.Value;
                comando.Parameters.Add("@documento", SqlDbType.VarChar, 30).Value = (object)e.Documento ?? DBNull.Value;
                comando.Parameters.Add("@usuario", SqlDbType.VarChar, 50).Value = e.Usuario;
                comando.Parameters.Add("@imputado", SqlDbType.VarChar, 50).Value = (object)e.Imputado ?? DBNull.Value;
                comando.Parameters.Add("@detalle", SqlDbType.NVarChar, RegistroCalidad.LONGITUD_DETALLE).Value = (object)e.Detalle ?? DBNull.Value;
                SqlParameter importe = comando.Parameters.Add("@importe", SqlDbType.Money);
                importe.Value = (object)e.Importe ?? DBNull.Value;
                conexion.Open();
                _ = comando.ExecuteNonQuery();
            }
        }
    }
}
