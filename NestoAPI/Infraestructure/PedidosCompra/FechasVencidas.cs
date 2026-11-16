using NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento;
using NestoAPI.Infraestructure.PedidosCompra.PlazosEntrega;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Infraestructure.PedidosVenta.FotosFechaEntrega;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosCompra
{
    /// <summary>NestoAPI#865: de dónde sale la fecha nueva de una línea de compra con la fecha de recepción vencida.</summary>
    public enum OrigenFechaRecalculada
    {
        /// <summary>Un correo de seguimiento (#857) del pedido dice cuándo llega (hoy o después).</summary>
        CorreoSeguimiento,
        /// <summary>De las recepciones del proveedor que tardaron más de lo que ya lleva esperando este pedido, el P80.</summary>
        HistorialProveedor,
        /// <summary>Pocos casos del proveedor: hoy + el P80 de todos los proveedores (fila «*» de dbo.ProveedoresPlazosEntrega).</summary>
        PlazoGenerico
    }

    /// <summary>NestoAPI#865: la fecha nueva de una línea vencida y con qué cifras se ha sacado.</summary>
    public class RecalculoFechaVencida
    {
        public DateTime FechaAnterior { get; set; }
        public DateTime FechaNueva { get; set; }
        public OrigenFechaRecalculada Origen { get; set; }
        /// <summary>Días desde que se pidió (CabPedidoCmp.[Fecha Modificación]) hasta hoy. Null si no se sabe.</summary>
        public int? DiasEsperando { get; set; }
        /// <summary>Recepciones del proveedor (24 meses) que tardaron MÁS de <see cref="DiasEsperando"/>.</summary>
        public int Casos { get; set; }
        /// <summary>Historial: el P80 de esos casos (días desde el pedido). Genérico: el P80 de todos. Null con correo.</summary>
        public int? DiasP80 { get; set; }
        /// <summary>La fecha que salía antes de ponerle el suelo del siguiente laborable.</summary>
        public DateTime FechaEstimada { get; set; }
        /// <summary>La estimación caía antes del siguiente laborable de Algete y se ha llevado a ese día.</summary>
        public bool AjustadaAlLaborable { get; set; }
        /// <summary>Con correo: la agencia que lo dice (null si no se sabe).</summary>
        public string Agencia { get; set; }
    }

    /// <summary>
    /// NestoAPI#865 (Carlos 16/11/26): la fecha nueva de una línea de compra en el aire cuya fecha de recepción ya ha pasado. PURA.
    /// <list type="number">
    /// <item>Si un correo de seguimiento del pedido (#857) dice que llega hoy o después, ese día (<see cref="ReglaFechaRecepcionPorCorreo.DiaDeLlegada"/>).
    /// Si su fecha también ha pasado, es «sin noticias».</item>
    /// <item>Sin noticias, «según lo ya esperado»: de las recepciones del proveedor (24 meses, las de <see cref="CalculoPlazosProveedores"/>)
    /// que tardaron MÁS días de los que ya lleva esperando este pedido, el P80, contado desde el día en que se pidió. Con menos de
    /// <see cref="CASOS_MINIMOS"/> casos, hoy + el P80 genérico.</item>
    /// <item>Lo estimado nunca antes del siguiente laborable de Algete (el correo sí puede decir «hoy»).</item>
    /// </list>
    /// Sin correo útil, sin casos suficientes y sin P80 genérico: null (no se cambia).
    /// </summary>
    public static class ReglaFechaVencida
    {
        public const int CASOS_MINIMOS = CalculoPlazosProveedores.RECEPCIONES_MINIMAS;
        public const double PERCENTIL = 0.8;
        private const int TOPE_LABORABLE = 30;

        public static RecalculoFechaVencida Calcular(DateTime hoy, DateTime fechaAnterior, DateTime? fechaPedido, IEnumerable<int> plazosProveedor,
            int? p80Generico, DateTime? llegadaCorreo, string agenciaCorreo, Func<DateTime, bool> esFestivo)
        {
            hoy = hoy.Date;
            int? esperando = fechaPedido.HasValue ? Math.Max(0, (hoy - fechaPedido.Value.Date).Days) : (int?)null;
            var resultado = new RecalculoFechaVencida { FechaAnterior = fechaAnterior.Date, DiasEsperando = esperando };
            if (llegadaCorreo.HasValue && llegadaCorreo.Value.Date >= hoy)
            {
                resultado.Origen = OrigenFechaRecalculada.CorreoSeguimiento;
                resultado.FechaEstimada = resultado.FechaNueva = llegadaCorreo.Value.Date;
                resultado.Agencia = string.IsNullOrWhiteSpace(agenciaCorreo) ? null : agenciaCorreo.Trim();
                return resultado;
            }

            List<int> casos = esperando.HasValue
                ? (plazosProveedor ?? Enumerable.Empty<int>()).Where(d => d > esperando.Value).ToList()
                : new List<int>();
            resultado.Casos = casos.Count;
            if (casos.Count >= CASOS_MINIMOS)
            {
                resultado.Origen = OrigenFechaRecalculada.HistorialProveedor;
                resultado.DiasP80 = ReglasFotosFechaEntrega.Percentil(casos, PERCENTIL);
                resultado.FechaEstimada = fechaPedido.Value.Date.AddDays(resultado.DiasP80.Value);
            }
            else if (p80Generico.HasValue)
            {
                resultado.Origen = OrigenFechaRecalculada.PlazoGenerico;
                resultado.DiasP80 = Math.Max(0, p80Generico.Value);
                resultado.FechaEstimada = hoy.AddDays(resultado.DiasP80.Value);
            }
            else
            {
                return null;
            }
            DateTime suelo = SiguienteLaborable(hoy, esFestivo);
            resultado.AjustadaAlLaborable = resultado.FechaEstimada < suelo;
            resultado.FechaNueva = resultado.AjustadaAlLaborable ? suelo : resultado.FechaEstimada;
            return resultado;
        }

        /// <summary>El primer día después de hoy en que Algete trabaja (<paramref name="esFestivo"/> cuenta sábados, domingos y festivos).</summary>
        public static DateTime SiguienteLaborable(DateTime hoy, Func<DateTime, bool> esFestivo)
        {
            DateTime dia = hoy.Date.AddDays(1);
            for (int i = 0; i < TOPE_LABORABLE && esFestivo != null && esFestivo(dia); i++)
            {
                dia = dia.AddDays(1);
            }
            return dia;
        }

        /// <summary>Los días que tardó cada recepción del proveedor (los negativos, pedido modificado después del albarán, no valen).</summary>
        public static Dictionary<string, List<int>> PlazosPorProveedor(IEnumerable<RecepcionCompra> recepciones) =>
            (recepciones ?? Enumerable.Empty<RecepcionCompra>())
                .Where(r => r != null && !string.IsNullOrWhiteSpace(r.Proveedor))
                .Select(r => new { Proveedor = r.Proveedor.Trim(), Dias = (r.FechaAlbaran.Date - r.FechaPedido.Date).Days })
                .Where(r => r.Dias >= 0)
                .GroupBy(r => r.Proveedor, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(r => r.Dias).ToList(), StringComparer.OrdinalIgnoreCase);

        /// <summary>¿Desde el 2.º vencimiento? Entonces hay que llamar al proveedor.</summary>
        public static bool HayQueLlamar(int vencimiento) => vencimiento >= 2;
    }

    /// <summary>
    /// NestoAPI#865: los correos de seguimiento guardados (#857) por pedido de compra («empresa/pedido»). PURA. Por envío
    /// (<see cref="MismoEnvio"/>): si uno está aplicado, todo el envío es de ese pedido; si no, solo si es inequívoco, no se ha
    /// descartado y no ha llegado ya (lo mismo que decide el job antes de poner la fecha). Las salidas nuestras y los descartados no cuentan.
    /// </summary>
    public static class CorreosDelPedidoCompra
    {
        public static Dictionary<string, List<CorreoSeguimientoFila>> Agrupar(IEnumerable<CorreoSeguimientoFila> correos)
        {
            var porPedido = new Dictionary<string, List<CorreoSeguimientoFila>>(StringComparer.OrdinalIgnoreCase);
            foreach (List<CorreoSeguimientoFila> grupo in MismoEnvio.Agrupar(correos))
            {
                DecisionEnvio decision = MismoEnvio.Decidir(grupo);
                string clave = null;
                if (decision.Aplicado != null)
                {
                    clave = AsignadorPedidosCompra.Clave(decision.Aplicado.EmpresaAplicada, decision.Aplicado.PedidoAplicado.Value);
                }
                else if (!decision.Descartado && !decision.YaLlego && decision.Unico != null)
                {
                    clave = AsignadorPedidosCompra.Clave(decision.Unico.Empresa, decision.Unico.Pedido);
                }
                if (clave == null)
                {
                    continue;
                }
                if (!porPedido.TryGetValue(clave, out List<CorreoSeguimientoFila> lista))
                {
                    porPedido[clave] = lista = new List<CorreoSeguimientoFila>();
                }
                lista.AddRange(decision.Correos.Where(f => f.Descartado != true));
            }
            return porPedido;
        }

        /// <summary>La agencia del correo más reciente que la diga.</summary>
        public static string Agencia(IEnumerable<CorreoSeguimientoFila> correos) =>
            (correos ?? Enumerable.Empty<CorreoSeguimientoFila>()).Where(f => f != null && !string.IsNullOrWhiteSpace(f.Agencia))
                .OrderByDescending(f => f.Recibido).ThenByDescending(f => f.Id).Select(f => f.Agencia.Trim()).FirstOrDefault();
    }

    /// <summary>NestoAPI#865: los textos de la fecha recalculada en el correo de proveedores vencidos (de usted, sin nombres de tablas). PUROS.</summary>
    public static class TextosFechaVencida
    {
        private static readonly CultureInfo es = new CultureInfo("es-ES");

        public const string SIN_RECALCULAR = "No se ha podido calcular una fecha nueva: no hay datos de cuánto suele tardar. Póngala usted en el pedido de compra.";
        public const string NO_ESCRITA = "No se ha cambiado: mientras tanto alguien ha cambiado la fecha o ha recibido la línea.";
        public const string ERROR = "No se ha podido cambiar la fecha por un error (queda apuntado). Póngala usted en el pedido de compra si la sabe.";

        public static string Dia(DateTime fecha) => fecha.ToString("dd/MM/yyyy", es);

        /// <summary>«Había pasado la fecha (13/11/2026); la hemos cambiado al 27/11/2026: …».</summary>
        public static string Linea(RecalculoFechaVencida r) =>
            r == null ? SIN_RECALCULAR : $"Había pasado la fecha ({Dia(r.FechaAnterior)}); la hemos cambiado al {Dia(r.FechaNueva)}: {Motivo(r)}.";

        /// <summary>Por qué esa fecha, con las cifras.</summary>
        public static string Motivo(RecalculoFechaVencida r)
        {
            if (r == null)
            {
                return string.Empty;
            }
            string motivo;
            switch (r.Origen)
            {
                case OrigenFechaRecalculada.CorreoSeguimiento:
                    motivo = r.Agencia == null
                        ? "lo dice el correo de seguimiento del envío"
                        : $"lo dice el correo de seguimiento de {r.Agencia}";
                    break;
                case OrigenFechaRecalculada.HistorialProveedor:
                    motivo = $"lleva {Dias(r.DiasEsperando.Value)} esperando y, de las {r.Casos} veces que este proveedor tardó más de " +
                        $"{Dias(r.DiasEsperando.Value)}, el 80 % llegó en {Dias(r.DiasP80.Value)} o menos desde que se le pidió";
                    break;
                default:
                    string poco = !r.DiasEsperando.HasValue
                        ? "no consta cuándo se pidió"
                        : r.Casos == 0
                            ? $"lleva {Dias(r.DiasEsperando.Value)} esperando y en los dos últimos años este proveedor nunca tardó tanto"
                            : $"lleva {Dias(r.DiasEsperando.Value)} esperando y en los dos últimos años este proveedor solo tardó tanto " +
                              $"{(r.Casos == 1 ? "una vez" : $"{r.Casos} veces")} (muy pocas para fiarse)";
                    motivo = $"{poco}, así que le damos desde hoy lo que suelen tardar todos los proveedores (el 80 % llega en {Dias(r.DiasP80 ?? 0)} o menos)";
                    break;
            }
            if (r.AjustadaAlLaborable)
            {
                motivo += $"; salía el {Dia(r.FechaEstimada)}, pero no puede llegar antes del siguiente día laborable";
            }
            return motivo;
        }

        public static string Llamar(int vencimiento) =>
            $"Hay que llamar al proveedor: es la {vencimiento}.ª vez que pasa la fecha de esta línea.";

        private static string Dias(int dias) => dias == 1 ? "1 día" : $"{dias} días";
    }

    /// <summary>NestoAPI#865: el rastro en Modificaciones (una fila por línea recalculada) y la cuenta de vencimientos. PURO.</summary>
    public static class RastroFechaRecalculada
    {
        public const string TABLA = "CompraFechaRecalculada";
        /// <summary>LinPedidoCmp.Usuario (varchar(30)) y Modificaciones.Usuario.</summary>
        public const string USUARIO = "Fecha recalculada (vencida)";
        /// <summary>Cuántos días atrás se miran las filas del rastro para contar vencimientos (una línea no vive tanto en el aire).</summary>
        public const int DIAS_REINCIDENCIA = 365;

        public static string Anterior(LineaCompraVencida linea) => JsonConvert.SerializeObject(new
        {
            linea.Clave,
            Empresa = linea.Empresa?.Trim(),
            linea.Pedido,
            NumeroOrden = linea.Orden,
            linea.Producto,
            FechaRecepcion = linea.FechaPrevista.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        });

        public static string Nuevo(LineaCompraVencida linea, RecalculoFechaVencida r, int vencimiento) => JsonConvert.SerializeObject(new
        {
            linea.Clave,
            Empresa = linea.Empresa?.Trim(),
            linea.Pedido,
            NumeroOrden = linea.Orden,
            linea.Producto,
            Proveedor = linea.Proveedor?.Trim(),
            FechaRecepcion = r.FechaNueva.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            FechaAnterior = r.FechaAnterior.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Origen = r.Origen.ToString(),
            r.DiasEsperando,
            r.Casos,
            r.DiasP80,
            FechaEstimada = r.FechaEstimada.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            r.AjustadaAlLaborable,
            r.Agencia,
            Vencimiento = vencimiento,
            Motivo = TextosFechaVencida.Linea(r)
        });

        /// <summary>Clave de la línea («1/220515/3») → cuántas veces se ha recalculado ya (filas del rastro). Lo ilegible no cuenta.</summary>
        public static Dictionary<string, int> ContarPorLinea(IEnumerable<string> nuevos)
        {
            var cuenta = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string nuevo in nuevos ?? Enumerable.Empty<string>())
            {
                string clave;
                try
                {
                    clave = string.IsNullOrWhiteSpace(nuevo) ? null : (string)JObject.Parse(nuevo)["Clave"];
                }
                catch (JsonException)
                {
                    clave = null;
                }
                if (string.IsNullOrWhiteSpace(clave))
                {
                    continue;
                }
                cuenta[clave.Trim()] = (cuenta.TryGetValue(clave.Trim(), out int n) ? n : 0) + 1;
            }
            return cuenta;
        }
    }

    /// <summary>NestoAPI#865: lo que ha pasado con una línea vencida.</summary>
    public class ResultadoFechaVencida
    {
        /// <summary>La fecha nueva y por qué (null = sin datos para recalcular).</summary>
        public RecalculoFechaVencida Recalculo { get; set; }
        /// <summary>Se ha escrito en la línea.</summary>
        public bool Cambiada { get; set; }
        /// <summary>Qué vencimiento es este (1 = el primero).</summary>
        public int Vencimiento { get; set; } = 1;
        public bool HayQueLlamar => ReglaFechaVencida.HayQueLlamar(Vencimiento);
        /// <summary>Si no se ha cambiado, por qué (texto para el correo).</summary>
        public string Problema { get; set; }

        /// <summary>El texto de la columna «Nueva fecha».</summary>
        public string Texto => Cambiada ? TextosFechaVencida.Linea(Recalculo) : Problema ?? TextosFechaVencida.SIN_RECALCULAR;
    }

    /// <summary>
    /// NestoAPI#865: el interruptor ParametrosUsuario empresa 1 «(defecto)» <see cref="CLAVE"/>: sin fila o «1», encendido; «0», false,
    /// no, off o apagado, apagado (el correo de siempre). Se lee en cada pasada, sin caché. Si no se puede leer, apagado (no se escribe).
    /// </summary>
    public static class InterruptorRecalcularFechasVencidas
    {
        public const string CLAVE = "Compras.RecalcularFechasVencidas";
        private const string USUARIO_DEFECTO = "(defecto)";

        internal static bool Interpretar(string valor) => ServicioNoEstaSalida.Interpretar(valor);

        public static bool Encendido(Action<Exception> registrar)
        {
            try
            {
                return Interpretar(NestoAPI.Controllers.ParametrosUsuarioController.LeerParametroSinDefecto(
                    Constantes.Empresas.EMPRESA_POR_DEFECTO, USUARIO_DEFECTO, CLAVE));
            }
            catch (Exception ex)
            {
                registrar?.Invoke(new Exception($"[Fechas vencidas #865] No se ha podido leer el interruptor {CLAVE}: no se recalcula. {ex.Message}", ex));
                return false;
            }
        }
    }

    /// <summary>
    /// NestoAPI#865 (corte 1): en el job del correo de proveedores vencidos, antes de mandarlo, a cada línea en el aire con la fecha
    /// pasada se le pone una nueva (<see cref="ReglaFechaVencida"/>) en LinPedidoCmp.FechaRecepción (+Usuario, [Fecha Modificación]), con
    /// rastro en Modificaciones (<see cref="RastroFechaRecalculada"/>) y la promesa en riesgo (#693) como la fase 3 de #857. Se recalcula
    /// cada vez; desde el 2.º vencimiento de la misma línea, el correo dice que hay que llamar al proveedor. Nunca lanza: lo que falla
    /// se apunta y esa línea sale en el correo como siempre.
    /// </summary>
    public class ServicioFechasVencidas
    {
        public const int MESES_HISTORICO = CalculoPlazosProveedores.MESES_HISTORICO;
        /// <summary>Correos de seguimiento que se miran (una entrega estimada puede estar hasta 60 días por delante del correo).</summary>
        public const int DIAS_CORREOS = 60;

        private readonly IRepositorioProveedorVencido repositorio;
        private readonly IRepositorioPlazosProveedores plazos;
        private readonly IRepositorioCorreosSeguimiento correos;
        private readonly Func<DateTime, bool> esFestivo;
        private readonly Func<bool> interruptor;
        private readonly Action<string, IEnumerable<string>, string, IDictionary<string, CausaPromesaRiesgo>> anotarPromesaRiesgo;
        private readonly Action<Exception> registrar;

        /// <param name="correos">Null = sin correos de seguimiento (todo «sin noticias»).</param>
        /// <param name="esFestivo">Festivo de Algete, con sábados y domingos.</param>
        /// <param name="interruptor">Por defecto, <see cref="InterruptorRecalcularFechasVencidas"/>.</param>
        public ServicioFechasVencidas(IRepositorioProveedorVencido repositorio, IRepositorioPlazosProveedores plazos, IRepositorioCorreosSeguimiento correos,
            Func<DateTime, bool> esFestivo, Func<bool> interruptor = null,
            Action<string, IEnumerable<string>, string, IDictionary<string, CausaPromesaRiesgo>> anotarPromesaRiesgo = null,
            Action<Exception> registrar = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.plazos = plazos;
            this.correos = correos;
            this.esFestivo = esFestivo ?? (d => d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday);
            this.registrar = registrar ?? (_ => { });
            this.interruptor = interruptor ?? (() => InterruptorRecalcularFechasVencidas.Encendido(this.registrar));
            this.anotarPromesaRiesgo = anotarPromesaRiesgo ?? EventosPromesaRiesgo.Anotar;
        }

        /// <summary>
        /// Recalcula y escribe. Devuelve, por clave de línea (<see cref="LineaCompraVencida.Clave"/>), lo que ha pasado; null si el
        /// interruptor está apagado (el correo, como siempre).
        /// </summary>
        public Dictionary<string, ResultadoFechaVencida> Recalcular(List<LineaCompraVencida> lineas, DateTime hoy)
        {
            if (!interruptor())
            {
                return null;
            }
            hoy = hoy.Date;
            var resultados = new Dictionary<string, ResultadoFechaVencida>(StringComparer.OrdinalIgnoreCase);
            List<LineaCompraVencida> lista = (lineas ?? new List<LineaCompraVencida>()).Where(l => l != null).ToList();
            if (!lista.Any())
            {
                return resultados;
            }

            int? p80Generico = Leer(() => plazos == null ? null : new PlazosProveedores(plazos.LeerPlazos(Constantes.Empresas.EMPRESA_POR_DEFECTO)).Generico?.DiasP80,
                "el plazo genérico de los proveedores");
            Dictionary<string, List<int>> plazosPorProveedor = Leer(() => plazos == null ? null : ReglaFechaVencida.PlazosPorProveedor(
                plazos.LeerRecepcionesDeProveedores(Constantes.Empresas.EMPRESA_POR_DEFECTO, hoy.AddMonths(-MESES_HISTORICO), lista.Select(l => l.Proveedor))),
                "las recepciones de los proveedores") ?? new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<CorreoSeguimientoFila>> correosPorPedido = Leer(() => LeerCorreos(hoy), "los correos de seguimiento")
                ?? new Dictionary<string, List<CorreoSeguimientoFila>>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> anteriores = Leer(() => RastroFechaRecalculada.ContarPorLinea(
                repositorio.LeerRecalculosAnteriores(hoy.AddDays(-RastroFechaRecalculada.DIAS_REINCIDENCIA))), "los recálculos anteriores")
                ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (LineaCompraVencida linea in lista)
            {
                var resultado = new ResultadoFechaVencida
                {
                    Vencimiento = (anteriores.TryGetValue(linea.Clave, out int previas) ? previas : 0) + 1
                };
                resultados[linea.Clave] = resultado;
                try
                {
                    correosPorPedido.TryGetValue(AsignadorPedidosCompra.Clave(linea.Empresa, linea.Pedido), out List<CorreoSeguimientoFila> suyos);
                    resultado.Recalculo = ReglaFechaVencida.Calcular(hoy, linea.FechaPrevista, linea.FechaPedido,
                        linea.Proveedor != null && plazosPorProveedor.TryGetValue(linea.Proveedor.Trim(), out List<int> dias) ? dias : null,
                        p80Generico, ReglaFechaRecepcionPorCorreo.DiaDeLlegada(suyos), CorreosDelPedidoCompra.Agencia(suyos), esFestivo);
                    if (resultado.Recalculo == null)
                    {
                        resultado.Problema = TextosFechaVencida.SIN_RECALCULAR;
                        continue;
                    }
                    resultado.Cambiada = repositorio.GuardarFechaRecalculada(linea, resultado.Recalculo.FechaNueva, RastroFechaRecalculada.USUARIO,
                        RastroFechaRecalculada.Anterior(linea), RastroFechaRecalculada.Nuevo(linea, resultado.Recalculo, resultado.Vencimiento));
                    if (!resultado.Cambiada)
                    {
                        resultado.Problema = TextosFechaVencida.NO_ESCRITA;
                    }
                }
                catch (Exception ex)
                {
                    resultado.Cambiada = false;
                    resultado.Problema = TextosFechaVencida.ERROR;
                    registrar(new Exception($"[Fechas vencidas #865] No se ha podido recalcular la línea {linea.Clave}: {ex.Message}", ex));
                }
            }

            foreach (IGrouping<string, LineaCompraVencida> pedido in lista.Where(l => resultados[l.Clave].Cambiada)
                .GroupBy(l => AsignadorPedidosCompra.Clave(l.Empresa, l.Pedido)))
            {
                AnotarPromesaRiesgo(pedido.ToList(), resultados);
            }
            return resultados;
        }

        /// <summary>NestoAPI#693: la fecha nueva retrasa lo que esperaban pedidos de venta (el motor contaba con «mañana»).</summary>
        private void AnotarPromesaRiesgo(List<LineaCompraVencida> lineas, Dictionary<string, ResultadoFechaVencida> resultados)
        {
            LineaCompraVencida primera = lineas.First();
            try
            {
                var porOrden = lineas.ToDictionary(l => l.Orden, l => resultados[l.Clave].Recalculo.FechaNueva);
                _ = CambiosCompraPromesaRiesgo.AnotarCambioFecha(primera.Empresa?.Trim(), primera.Pedido, lineas.Select(l => new LineaCompraPromesaRiesgo
                {
                    Id = l.Orden, Producto = l.Producto, Estado = l.Estado, Enviado = true, Cantidad = l.Cantidad, FechaRecepcion = l.FechaPrevista
                }), l => porOrden[l.Id], $"pedido a proveedor {primera.Pedido}: fecha vencida recalculada", anotarPromesaRiesgo);
            }
            catch (Exception ex)
            {
                registrar(new Exception($"[Fechas vencidas #865] No se ha podido anotar el cambio del pedido {primera.Pedido} para la promesa en riesgo: {ex.Message}", ex));
            }
        }

        private Dictionary<string, List<CorreoSeguimientoFila>> LeerCorreos(DateTime hoy)
        {
            if (correos == null || !correos.TablasExisten() || !correos.AplicacionDisponible())
            {
                return null;
            }
            List<CorreoSeguimientoFila> filas = correos.LeerCorreos(hoy.AddDays(-DIAS_CORREOS)).ConfigureAwait(false).GetAwaiter().GetResult();
            return CorreosDelPedidoCompra.Agrupar(filas);
        }

        /// <summary>Una lectura de apoyo: si falla, se apunta y se sigue sin ella (default).</summary>
        private T Leer<T>(Func<T> lectura, string que)
        {
            try
            {
                return lectura();
            }
            catch (Exception ex)
            {
                registrar(new Exception($"[Fechas vencidas #865] No se han podido leer {que}: {ex.Message}", ex));
                return default(T);
            }
        }
    }
}
