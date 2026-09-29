using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.PreciosMedios
{
    /// <summary>Productos que tocan en una pasada nocturna, y por qué.</summary>
    public sealed class SeleccionIncrementalPrecioMedio
    {
        /// <summary>Unión ordenada y sin repetidos.</summary>
        public List<string> Productos { get; set; } = new List<string>();

        /// <summary>Por compras (o facturas de compra) modificadas desde la última pasada.</summary>
        public int PorCompras { get; set; }

        /// <summary>
        /// Por movimientos de stock grabados desde la última pasada (Nº Orden de ExtractoProducto mayor que el último
        /// revisado) con fecha en o antes de una compra facturada (riesgo 2).
        /// </summary>
        public int PorMovimientos { get; set; }
    }

    /// <summary>Resumen de una empresa en una pasada nocturna del incremental.</summary>
    public sealed class ResumenEmpresaIncrementalPrecioMedio
    {
        public const int MAXIMO_ERRORES_EN_RESUMEN = 20;

        public string Empresa { get; set; }
        public string EmpresaEspejo { get; set; }
        public int PorCompras { get; set; }
        public int PorMovimientos { get; set; }
        public int Seleccionados { get; set; }
        public int Revisados { get; set; }
        public int ConCambios { get; set; }
        public int SinCambios { get; set; }
        public int NoProcesados { get; set; }
        public int Errores { get; set; }
        public int FilasProductos { get; set; }
        public int FilasCompras { get; set; }
        public int FilasVentas { get; set; }
        public double Segundos { get; set; }
        public bool Interrumpida { get; set; }
        public List<string> PrimerosErrores { get; } = new List<string>();
    }

    /// <summary>Resumen de una pasada nocturna del incremental (va a ELMAH como información).</summary>
    public sealed class ResumenPasadaIncrementalPrecioMedio
    {
        /// <summary>Hora del servidor de BD al empezar.</summary>
        public DateTime Inicio { get; set; }

        /// <summary>Se recalcula lo modificado desde aquí.</summary>
        public DateTime Desde { get; set; }

        /// <summary>De dónde sale <see cref="Desde"/>: «última pasada», «SP» o «domingo».</summary>
        public string OrigenDesde { get; set; }

        /// <summary>Nueva marca guardada (nula si no se ha guardado: pasada interrumpida o aplazada).</summary>
        public DateTime? MarcaGuardada { get; set; }

        /// <summary>
        /// Riesgo 2: se miran los apuntes de ExtractoProducto con Nº Orden mayor que este (el último revisado por la
        /// pasada anterior). Nulo si no había marca: esa noche no se miran movimientos y se empieza a seguir desde
        /// <see cref="HastaNumOrden"/> (lo anterior lo cubre el SP de los domingos).
        /// </summary>
        public int? DesdeNumOrden { get; set; }

        /// <summary>Máximo Nº Orden de ExtractoProducto (de las empresas y sus espejos) al empezar la pasada. Nulo si no hay apuntes.</summary>
        public int? HastaNumOrden { get; set; }

        /// <summary>Nº Orden guardado para la próxima pasada (nulo si no se ha guardado).</summary>
        public int? NumOrdenGuardado { get; set; }

        /// <summary>Motivo por el que no se ha hecho la pasada (p. ej. el SP del domingo sigue corriendo). Nulo si se ha hecho.</summary>
        public string Aplazada { get; set; }

        public List<ResumenEmpresaIncrementalPrecioMedio> Empresas { get; } = new List<ResumenEmpresaIncrementalPrecioMedio>();

        public bool Interrumpida => Empresas.Any(e => e.Interrumpida);
        public int Errores => Empresas.Sum(e => e.Errores);

        public override string ToString()
        {
            if (Aplazada != null)
            {
                return "Incremental de precios medios APLAZADO: " + Aplazada;
            }
            return string.Format(CultureInfo.InvariantCulture, "Incremental de precios medios (desde {0:dd/MM/yyyy HH:mm:ss}, {1}; marca {2}; {3}): ",
                    Desde, OrigenDesde, MarcaGuardada.HasValue ? MarcaGuardada.Value.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture) : "NO guardada",
                    TextoNumOrden()) +
                string.Join(" | ", Empresas.Select(e => string.Format(CultureInfo.InvariantCulture,
                    "empresa {0}: {1}/{2} productos ({3} por compras, {4} por movimientos con fecha pasada) en {5:0}s{6}; con cambios {7}, sin cambios {8}, " +
                    "no procesados {9}, ERRORES {10}; filas Productos {11}, LinPedidoCmp {12}, LinPedidoVta {13}{14}",
                    e.Empresa, e.Revisados, e.Seleccionados, e.PorCompras, e.PorMovimientos, e.Segundos, e.Interrumpida ? " (INTERRUMPIDA por tiempo)" : "",
                    e.ConCambios, e.SinCambios, e.NoProcesados, e.Errores, e.FilasProductos, e.FilasCompras, e.FilasVentas,
                    e.PrimerosErrores.Any() ? "; errores: " + string.Join("; ", e.PrimerosErrores) : "")));
        }

        private string TextoNumOrden()
        {
            string guardado = NumOrdenGuardado.HasValue
                ? "guardado " + NumOrdenGuardado.Value.ToString(CultureInfo.InvariantCulture)
                : "NO guardado";
            if (!HastaNumOrden.HasValue)
            {
                return "Nº Orden de ExtractoProducto: sin apuntes, " + guardado;
            }
            if (!DesdeNumOrden.HasValue)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "Nº Orden de ExtractoProducto: SIN marca previa, esta noche no se miran movimientos y se empieza a seguir desde {0} (lo anterior lo cubre el SP); {1}",
                    HastaNumOrden.Value, guardado);
            }
            return string.Format(CultureInfo.InvariantCulture, "Nº Orden de ExtractoProducto {0} → {1}; {2}",
                DesdeNumOrden.Value, HastaNumOrden.Value, guardado);
        }
    }

    /// <summary>
    /// Issue #547, corte (c): orquesta el incremental de precios medios. Decide QUÉ productos recalcular (la pasada
    /// nocturna y los encolados al facturar compras) y delega la escritura en <see cref="EscritorPreciosMedios"/>.
    /// No sabe nada del interruptor ni de Hangfire (eso es <see cref="PreciosMediosIncrementalJobsService"/>).
    /// </summary>
    public class ServicioIncrementalPreciosMedios
    {
        /// <summary>
        /// Margen hacia atrás de la marca de la última pasada: un cambio grabado en una transacción larga que empezó
        /// antes del inicio de la pasada (y confirmó después) tiene una «Fecha Modificación» anterior al inicio. Repasar
        /// unos minutos de más es gratis (con paridad no se escribe nada).
        /// </summary>
        internal static readonly TimeSpan MARGEN_MARCA = TimeSpan.FromMinutes(10);

        private readonly IRepositorioEscrituraPreciosMedios escritura;
        private readonly IRepositorioPreciosMedios lectura;
        private readonly EscritorPreciosMedios escritor;

        public ServicioIncrementalPreciosMedios(IRepositorioEscrituraPreciosMedios escritura, IRepositorioPreciosMedios lectura)
        {
            this.escritura = escritura ?? throw new ArgumentNullException(nameof(escritura));
            this.lectura = lectura ?? throw new ArgumentNullException(nameof(lectura));
            escritor = new EscritorPreciosMedios(escritura);
        }

        /// <summary>Última ejecución del SP de los domingos, o nulo si no se puede saber (sin permiso en msdb...).</summary>
        public EjecucionSPPreciosMedios UltimaEjecucionSP()
        {
            try
            {
                return lectura.UltimaEjecucionSP();
            }
            catch
            {
                return null;
            }
        }

        public string EmpresaEspejo(string empresa)
        {
            return lectura.EmpresaEspejo(empresa) ?? empresa;
        }

        /// <summary>
        /// Empresa que procesa el SP para una línea de <paramref name="empresa"/>: ella misma si es 1, 4 o 5; si es la
        /// espejo de alguna (la 3 de la 1), esa. Nula si el SP no la procesa (2, 6...).
        /// </summary>
        public string EmpresaPrincipal(string empresa)
        {
            string e = empresa?.Trim();
            if (ServicioSombraPreciosMedios.EMPRESAS.Contains(e))
            {
                return e;
            }
            return ServicioSombraPreciosMedios.EMPRESAS.FirstOrDefault(p => string.Equals(lectura.EmpresaEspejo(p)?.Trim(), e, StringComparison.Ordinal));
        }

        /// <summary>
        /// Productos de la pasada nocturna: compras modificadas desde <paramref name="desde"/> MÁS los que tienen
        /// movimientos de stock grabados desde la última pasada (Nº Orden en (<paramref name="desdeNumOrden"/>,
        /// <paramref name="hastaNumOrden"/>]) con fecha pasada (riesgo 2: cambian el stock a la fecha de una compra
        /// antigua sin tocar LinPedidoCmp). Sin <paramref name="desdeNumOrden"/> (primera noche) no se miran
        /// movimientos. Unión ordenada, sin repetidos ni espacios.
        /// </summary>
        public SeleccionIncrementalPrecioMedio SeleccionarProductos(string empresa, string espejo, DateTime desde, int? desdeNumOrden, int? hastaNumOrden)
        {
            List<string> porCompras = Limpiar(escritura.ProductosConComprasModificadas(empresa, espejo, desde));
            List<string> porMovimientos = desdeNumOrden.HasValue && hastaNumOrden.HasValue && hastaNumOrden.Value > desdeNumOrden.Value
                ? Limpiar(escritura.ProductosConMovimientosConFechaPasada(empresa, espejo, desdeNumOrden.Value, hastaNumOrden.Value))
                : new List<string>();
            return new SeleccionIncrementalPrecioMedio
            {
                PorCompras = porCompras.Count,
                PorMovimientos = porMovimientos.Count,
                Productos = porCompras.Union(porMovimientos, StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToList()
            };
        }

        public IReadOnlyList<string> ProductosDelPedidoCompra(string empresa, int pedido)
        {
            return Limpiar(escritura.ProductosDelPedidoCompra(empresa, pedido));
        }

        /// <summary>Recalcula y escribe un producto (la empresa puede venir como espejo: se traduce a la principal).</summary>
        public ResultadoEscrituraPrecioMedio RecalcularProducto(string empresa, string producto)
        {
            string principal = EmpresaPrincipal(empresa);
            if (principal == null)
            {
                return null; // empresa que el SP no procesa
            }
            return escritor.RecalcularProducto(principal, EmpresaEspejo(principal), producto);
        }

        /// <summary>
        /// Pasada nocturna. Si el SP del domingo sigue corriendo, se aplaza sin tocar nada (ni la marca). Si se acaba
        /// el tiempo, se deja la marca como estaba (la próxima noche repasa lo mismo: es idempotente). Los productos que
        /// fallan (bloqueo, interbloqueo...) se pasan a <paramref name="reintentar"/> y la marca sí avanza.
        /// </summary>
        public ResumenPasadaIncrementalPrecioMedio EjecutarPasadaNocturna(DateTime ahora, TimeSpan limite, Action<string, string> reintentar)
        {
            ResumenPasadaIncrementalPrecioMedio resumen = new ResumenPasadaIncrementalPrecioMedio();
            EjecucionSPPreciosMedios sp = UltimaEjecucionSP();
            if (sp != null && sp.EnEjecucion)
            {
                resumen.Aplazada = $"el SP «Precios Medios» sigue en ejecución (empezó el {sp.Inicio:dd/MM/yyyy HH:mm}); se repasará la próxima noche";
                return resumen;
            }

            resumen.Inicio = escritura.AhoraServidor();
            DateTime? marca = escritura.LeerUltimaPasada();
            if (marca.HasValue)
            {
                resumen.Desde = marca.Value;
                resumen.OrigenDesde = "última pasada";
            }
            else if (sp != null)
            {
                resumen.Desde = sp.Inicio;
                resumen.OrigenDesde = "inicio del SP";
            }
            else
            {
                resumen.Desde = PreciosMediosJobsService.CorteSP(ahora);
                resumen.OrigenDesde = "domingo 00:30";
            }
            resumen.DesdeNumOrden = escritura.LeerUltimoNumOrdenExtracto();
            resumen.HastaNumOrden = MaximoNumOrdenExtracto();

            Stopwatch reloj = Stopwatch.StartNew();
            foreach (string empresa in ServicioSombraPreciosMedios.EMPRESAS)
            {
                resumen.Empresas.Add(ProcesarEmpresa(empresa, resumen.Desde, resumen.DesdeNumOrden, resumen.HastaNumOrden, reloj, limite, reintentar));
                if (reloj.Elapsed > limite)
                {
                    break;
                }
            }

            bool completa = !resumen.Interrumpida && resumen.Empresas.Count == ServicioSombraPreciosMedios.EMPRESAS.Count;
            if (completa)
            {
                DateTime nueva = resumen.Inicio - MARGEN_MARCA;
                escritura.GuardarUltimaPasada(nueva);
                resumen.MarcaGuardada = nueva;
                if (resumen.HastaNumOrden.HasValue)
                {
                    escritura.GuardarUltimoNumOrdenExtracto(resumen.HastaNumOrden.Value);
                    resumen.NumOrdenGuardado = resumen.HastaNumOrden;
                }
            }
            return resumen;
        }

        /// <summary>
        /// Máximo Nº Orden de ExtractoProducto de las empresas del SP y sus espejos (una búsqueda por empresa sobre la
        /// clave primaria). Nº Orden es una identidad de toda la tabla, así que vale un único máximo para todas. Los
        /// apuntes que se graben mientras dura la pasada tendrán un Nº Orden mayor: los recoge la noche siguiente.
        /// </summary>
        private int? MaximoNumOrdenExtracto()
        {
            int? maximo = null;
            foreach (string e in ServicioSombraPreciosMedios.EMPRESAS.SelectMany(p => new[] { p, EmpresaEspejo(p) }).Distinct(StringComparer.Ordinal))
            {
                int? valor = escritura.MaximoNumOrdenExtracto(e);
                if (valor.HasValue && (!maximo.HasValue || valor.Value > maximo.Value))
                {
                    maximo = valor;
                }
            }
            return maximo;
        }

        private ResumenEmpresaIncrementalPrecioMedio ProcesarEmpresa(string empresa, DateTime desde, int? desdeNumOrden, int? hastaNumOrden,
            Stopwatch reloj, TimeSpan limite, Action<string, string> reintentar)
        {
            Stopwatch relojEmpresa = Stopwatch.StartNew();
            string espejo = EmpresaEspejo(empresa);
            ResumenEmpresaIncrementalPrecioMedio r = new ResumenEmpresaIncrementalPrecioMedio { Empresa = empresa, EmpresaEspejo = espejo };
            SeleccionIncrementalPrecioMedio seleccion = SeleccionarProductos(empresa, espejo, desde, desdeNumOrden, hastaNumOrden);
            r.PorCompras = seleccion.PorCompras;
            r.PorMovimientos = seleccion.PorMovimientos;
            r.Seleccionados = seleccion.Productos.Count;

            foreach (string producto in seleccion.Productos)
            {
                if (reloj.Elapsed > limite)
                {
                    r.Interrumpida = true;
                    break;
                }
                r.Revisados++;
                try
                {
                    ResultadoEscrituraPrecioMedio resultado = escritor.RecalcularProducto(empresa, espejo, producto);
                    if (resultado?.Plan == null || !resultado.Plan.Procesado)
                    {
                        r.NoProcesados++;
                    }
                    else if (resultado.HaCambiadoAlgo)
                    {
                        r.ConCambios++;
                    }
                    else
                    {
                        r.SinCambios++;
                    }
                    r.FilasProductos += resultado?.FilasProductos ?? 0;
                    r.FilasCompras += resultado?.FilasCompras ?? 0;
                    r.FilasVentas += resultado?.FilasVentas ?? 0;
                }
                catch (Exception ex)
                {
                    r.Errores++;
                    if (r.PrimerosErrores.Count < ResumenEmpresaIncrementalPrecioMedio.MAXIMO_ERRORES_EN_RESUMEN)
                    {
                        r.PrimerosErrores.Add(producto + ": " + ex.Message);
                    }
                    try
                    {
                        reintentar?.Invoke(empresa, producto);
                    }
                    catch
                    {
                        // Si ni siquiera se puede encolar, lo recoge la pasada del domingo (el SP sigue de red).
                    }
                }
            }
            r.Segundos = relojEmpresa.Elapsed.TotalSeconds;
            return r;
        }

        private static List<string> Limpiar(IEnumerable<string> productos)
        {
            return (productos ?? Enumerable.Empty<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }
    }
}
