using Hangfire;
using NestoAPI.Infraestructure.IndicadoresStock;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosCompra.PlazosEntrega
{
    /// <summary>Resultado del recálculo de dbo.ProveedoresPlazosEntrega.</summary>
    public class ResultadoRecalculoPlazos
    {
        public bool Hecho { get; set; }
        public int Proveedores { get; set; }
        public int ConPlazoPropio { get; set; }
        public int Recepciones { get; set; }
        public int? P80Generico { get; set; }
        public string Mensaje { get; set; }
    }

    /// <summary>
    /// NestoAPI#853: los plazos leídos de la tabla con caché de <see cref="DURACION"/> (el cálculo de la fecha de entrega se llama mucho: la
    /// plantilla de NestoApp). Null = sin tabla o vacía (todo como antes). Un fallo de lectura va a ELMAH una sola vez hasta que vuelva a ir bien.
    /// </summary>
    public static class CachePlazosProveedores
    {
        internal static readonly TimeSpan DURACION = TimeSpan.FromMinutes(10);
        private static readonly object cerrojo = new object();
        private static readonly Dictionary<string, (PlazosProveedores Plazos, DateTime LeidoUtc)> porEmpresa =
            new Dictionary<string, (PlazosProveedores, DateTime)>(StringComparer.OrdinalIgnoreCase);
        private static bool falloAvisado;

        /// <summary>La empresa con la que se guardan los plazos: la espejo cuenta con la principal.</summary>
        public static string EmpresaPlazos(string empresa)
        {
            string limpia = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            return limpia == Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO ? Constantes.Empresas.EMPRESA_POR_DEFECTO : limpia;
        }

        public static PlazosProveedores Obtener(Func<IRepositorioPlazosProveedores> repositorio, string empresa, Action<Exception> registrar)
        {
            string clave = EmpresaPlazos(empresa);
            lock (cerrojo)
            {
                if (porEmpresa.TryGetValue(clave, out (PlazosProveedores Plazos, DateTime LeidoUtc) entrada) && DateTime.UtcNow - entrada.LeidoUtc < DURACION)
                {
                    return entrada.Plazos;
                }
            }
            PlazosProveedores plazos;
            try
            {
                var leidos = new PlazosProveedores(repositorio().LeerPlazos(clave));
                plazos = leidos.TieneDatos ? leidos : null;
                falloAvisado = false;
            }
            catch (Exception ex)
            {
                plazos = null;
                if (!falloAvisado)
                {
                    falloAvisado = true;
                    try
                    {
                        registrar?.Invoke(new Exception($"[Plazos de proveedores #853] No se han podido leer los plazos: se sigue sin ellos (se avisa una vez). {ex.Message}", ex));
                    }
                    catch
                    {
                        // nunca rompe a quien llama
                    }
                }
            }
            lock (cerrojo)
            {
                porEmpresa[clave] = (plazos, DateTime.UtcNow);
            }
            return plazos;
        }

        public static void Invalidar()
        {
            lock (cerrojo)
            {
                porEmpresa.Clear();
            }
        }
    }

    /// <summary>NestoAPI#853: recálculo y lectura de dbo.ProveedoresPlazosEntrega.</summary>
    public class ServicioPlazosProveedores
    {
        public const string MENSAJE_SIN_TABLA = "Todavía no existe la tabla: hay que lanzar Scripts/Issue853_PlazosProveedoresYEnvios.sql.";

        private readonly IRepositorioPlazosProveedores repositorio;
        private readonly Func<DateTime> ahora;

        public ServicioPlazosProveedores(IRepositorioPlazosProveedores repositorio, Func<DateTime> ahora = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.ahora = ahora ?? (() => DateTime.Now);
        }

        public bool TablaExiste() => repositorio.TablaPlazosExiste();

        public List<PlazoProveedor> Leer(string empresa) => repositorio.LeerPlazos(CachePlazosProveedores.EmpresaPlazos(empresa))
            .OrderBy(p => p.EsGenerico ? 1 : 0).ThenByDescending(p => p.Recepciones).ThenBy(p => p.Proveedor).ToList();

        /// <summary>Recalcula y SUSTITUYE las filas de la empresa (en una transacción). Sin tabla, no hace nada.</summary>
        public ResultadoRecalculoPlazos Recalcular(string empresa)
        {
            string clave = CachePlazosProveedores.EmpresaPlazos(empresa);
            if (!repositorio.TablaPlazosExiste())
            {
                return new ResultadoRecalculoPlazos { Hecho = false, Mensaje = MENSAJE_SIN_TABLA };
            }
            DateTime momento = ahora();
            DateTime desde = momento.Date.AddMonths(-CalculoPlazosProveedores.MESES_HISTORICO);
            List<RecepcionCompra> recepciones = repositorio.LeerRecepciones(clave, desde);
            List<PedidoCompraDia> pedidos = repositorio.LeerPedidos(clave, desde);
            List<PlazoProveedor> plazos = CalculoPlazosProveedores.Calcular(clave, recepciones, pedidos, momento);
            repositorio.SustituirPlazos(clave, plazos);
            CachePlazosProveedores.Invalidar();
            PlazoProveedor generico = plazos.FirstOrDefault(p => p.EsGenerico);
            return new ResultadoRecalculoPlazos
            {
                Hecho = true,
                Proveedores = plazos.Count(p => !p.EsGenerico),
                ConPlazoPropio = plazos.Count(p => p.TienePlazoPropio),
                Recepciones = generico?.Recepciones ?? 0,
                P80Generico = generico?.DiasP80,
                Mensaje = $"Recalculados los plazos de {plazos.Count(p => !p.EsGenerico)} proveedores ({plazos.Count(p => p.TienePlazoPropio)} con plazo propio)."
            };
        }
    }

    /// <summary>
    /// NestoAPI#853: job nocturno <see cref="JOB_ID"/> (todos los días a las 3:40, Madrid) y el lanzamiento a mano (POST
    /// api/Compras/PlazosProveedores/Recalcular). Va en la cola de los cálculos nocturnos (<see cref="IndicadoresStockJobsService.COLA"/>).
    /// </summary>
    public static class PlazosProveedoresJobsService
    {
        public const string JOB_ID = "proveedores-plazos-entrega";
        public const string CRON = "40 3 * * *";
        private const string USUARIO_ELMAH = "Sistema (plazos de proveedores)";

        [Queue(IndicadoresStockJobsService.COLA)]
        [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
        [DisableConcurrentExecution(timeoutInSeconds: 1800)]
        public static void Recalcular()
        {
            try
            {
                ResultadoRecalculoPlazos resultado = new ServicioPlazosProveedores(RepositorioPlazosProveedores.DesdeConfiguracion())
                    .Recalcular(Constantes.Empresas.EMPRESA_POR_DEFECTO);
                Console.WriteLine($"[Plazos de proveedores #853] {resultado.Mensaje}");
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Plazos de proveedores #853] Error al recalcular: {ex.Message}", ex), USUARIO_ELMAH);
            }
        }
    }

    /// <summary>
    /// NestoAPI#853 (corte 2): la FechaRecepción de las líneas al ENVIAR un pedido de compra (PUT api/PedidosCompra con PathPedido) y su
    /// registro en dbo.PedidosCompraEnvios.
    /// <list type="bullet">
    /// <item>Con ParametrosUsuario empresa 1 «(defecto)» <see cref="CLAVE_INTERRUPTOR"/> = «1» (sin fila = apagado) y la tabla de plazos con
    /// datos: <see cref="ReglaFechaRecepcionEstimada"/>. Si no, lo de siempre: hoy + DíasEnServir.</item>
    /// <item>NUNCA rompe el envío: ante cualquier fallo, lo de siempre y ELMAH.</item>
    /// <item>El registro va con el interruptor encendido o apagado, siempre que exista la tabla; solo la primera vez de cada pedido.</item>
    /// </list>
    /// </summary>
    public class ServicioFechaRecepcionAlEnviar
    {
        public const string CLAVE_INTERRUPTOR = "Compras.FechaRecepcionPorPlazoReal";
        public const string USUARIO_DEFECTO = "(defecto)";
        /// <summary>Origen de la fecha puesta cuando no se aplica la regla (interruptor apagado, sin plazos o fallo).</summary>
        public const string ORIGEN_COMO_ANTES = "ComoAntes";
        private const string USUARIO_ELMAH = "Sistema (plazos de proveedores)";

        private readonly Func<IRepositorioPlazosProveedores> repositorio;
        private readonly Func<bool> interruptor;
        private readonly Func<DateTime> ahora;
        private readonly Action<Exception> registrar;
        private readonly Func<string, PlazosProveedores> plazos;

        private static readonly object cerrojoTabla = new object();
        private static bool? tablaEnvios;
        private static DateTime tablaEnviosComprobadaUtc;
        private static bool falloRegistroAvisado;

        public ServicioFechaRecepcionAlEnviar(Func<IRepositorioPlazosProveedores> repositorio = null, Func<bool> interruptor = null,
            Func<DateTime> ahora = null, Action<Exception> registrar = null, Func<string, PlazosProveedores> plazos = null)
        {
            this.repositorio = repositorio ?? (() => RepositorioPlazosProveedores.DesdeConfiguracion());
            this.registrar = registrar ?? (ex => ElmahHelper.Log(ex, USUARIO_ELMAH));
            this.interruptor = interruptor ?? (() => InterruptorEncendido(this.registrar));
            this.ahora = ahora ?? (() => DateTime.Now);
            this.plazos = plazos ?? (empresa => CachePlazosProveedores.Obtener(this.repositorio, empresa, this.registrar));
        }

        /// <summary>Encendido SOLO con «1».</summary>
        internal static bool Interpretar(string valor) => valor?.Trim() == "1";

        internal static bool InterruptorEncendido(Action<Exception> registrar)
        {
            try
            {
                return Interpretar(NestoAPI.Controllers.ParametrosUsuarioController.LeerParametroSinDefecto(
                    Constantes.Empresas.EMPRESA_POR_DEFECTO, USUARIO_DEFECTO, CLAVE_INTERRUPTOR));
            }
            catch (Exception ex)
            {
                registrar(new Exception($"[Plazos de proveedores #853] No se ha podido leer el interruptor {CLAVE_INTERRUPTOR}: se pone la fecha de siempre. {ex.Message}", ex));
                return false;
            }
        }

        /// <summary>La fecha que se pone (y la de la regla, para el registro). Nunca lanza.</summary>
        public FechaRecepcionEstimada Calcular(string empresa, string proveedor, int diasEnServir)
        {
            DateTime hoy = ahora().Date;
            var comoAntes = new FechaRecepcionEstimada { Fecha = hoy.AddDays(diasEnServir), Origen = ORIGEN_COMO_ANTES };
            try
            {
                PlazosProveedores leidos = plazos(empresa);
                if (leidos == null || !leidos.TieneDatos)
                {
                    return comoAntes;
                }
                FechaRecepcionEstimada regla = ReglaFechaRecepcionEstimada.Calcular(hoy, diasEnServir, leidos.DeProveedor(proveedor), leidos.Generico);
                if (!interruptor())
                {
                    comoAntes.FechaRegla = regla.FechaRegla;
                    return comoAntes;
                }
                return regla;
            }
            catch (Exception ex)
            {
                Avisar($"no se ha podido calcular la fecha de recepción por el plazo real del proveedor {proveedor?.Trim()}: se pone la de siempre", ex);
                return comoAntes;
            }
        }

        /// <summary>Apunta el envío en dbo.PedidosCompraEnvios (si existe). Nunca lanza.</summary>
        public void Registrar(string empresa, int pedido, string proveedor, int diasEnServir, FechaRecepcionEstimada fecha, string usuario)
        {
            try
            {
                if (fecha == null || !TablaEnviosExiste())
                {
                    return;
                }
                repositorio().RegistrarEnvio(new EnvioPedidoCompra
                {
                    Empresa = empresa?.Trim(),
                    Pedido = pedido,
                    Proveedor = proveedor?.Trim(),
                    FechaEnvio = ahora(),
                    FechaRecepcionPuesta = fecha.Fecha,
                    FechaRegla = fecha.FechaRegla,
                    FechaModelo = null,
                    DiasEnServir = diasEnServir,
                    Origen = fecha.Origen,
                    Usuario = usuario
                });
            }
            catch (Exception ex)
            {
                Avisar($"no se ha podido apuntar el envío del pedido de compra {pedido} en PedidosCompraEnvios", ex);
            }
        }

        private bool TablaEnviosExiste()
        {
            lock (cerrojoTabla)
            {
                if (tablaEnvios.HasValue && DateTime.UtcNow - tablaEnviosComprobadaUtc < CachePlazosProveedores.DURACION)
                {
                    return tablaEnvios.Value;
                }
            }
            bool existe = repositorio().TablaEnviosExiste();
            lock (cerrojoTabla)
            {
                tablaEnvios = existe;
                tablaEnviosComprobadaUtc = DateTime.UtcNow;
            }
            return existe;
        }

        /// <summary>Solo para tests.</summary>
        internal static void OlvidarComprobaciones()
        {
            lock (cerrojoTabla)
            {
                tablaEnvios = null;
                falloRegistroAvisado = false;
            }
        }

        private void Avisar(string que, Exception ex)
        {
            if (falloRegistroAvisado)
            {
                return;
            }
            falloRegistroAvisado = true;
            try
            {
                registrar(new Exception($"[Plazos de proveedores #853] {que} (se avisa una vez). {ex.Message}", ex));
            }
            catch
            {
                // nunca rompe el envío
            }
        }
    }
}
