using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;

namespace NestoAPI.Infraestructure.PreciosMedios
{
    /// <summary>Qué ha salido al comparar un producto.</summary>
    public enum ClasificacionSombraPrecioMedio
    {
        /// <summary>Todo igual al diezmilésimo y sin avisos.</summary>
        Igual,

        /// <summary>Todo igual, pero la calculadora avisa de algo (media negativa, albarán anterior sin facturar...).</summary>
        IgualConAvisos,

        /// <summary>
        /// Distinto de la BD, pero igual si se ignoran las facturas creadas después de la pasada del SP: el SP aún
        /// no las ha visto. Esperado; lo arregla el SP del domingo (o el incremental del corte c).
        /// </summary>
        Pendiente,

        /// <summary>
        /// Distinto, pero el producto tiene un empate que el SP resuelve de forma indeterminada (dos facturas a la
        /// misma hora, albarán anterior ambiguo): el C# desempata por nº de factura. Esperado; se documenta.
        /// </summary>
        Empate,

        /// <summary>Distinto sin explicación: fallo de paridad. Hay que sacar su fixture y arreglar la calculadora.</summary>
        Distinto,

        /// <summary>El SP no toca el producto (ficticio, excluido por montaje, sin compras...).</summary>
        NoProcesado,

        /// <summary>No tiene ficha en la empresa.</summary>
        SinFicha,

        /// <summary>Ha fallado la lectura o el cálculo.</summary>
        Error
    }

    /// <summary>Resultado de la sombra para un producto.</summary>
    public sealed class ResultadoSombraPrecioMedio
    {
        public string Empresa { get; set; }
        public string Producto { get; set; }
        public ClasificacionSombraPrecioMedio Clasificacion { get; set; }

        /// <summary>Líneas facturadas después de la pasada del SP (no las ha visto aún).</summary>
        public int LineasPendientes { get; set; }

        /// <summary>Cálculo con el historial actual (lo que el SP dejará el próximo domingo).</summary>
        public ResultadoPrecioMedio Calculo { get; set; }

        /// <summary>Diferencia que se registra: con el historial que vio el SP si hay pendientes, si no, con el actual.</summary>
        public DiferenciaPrecioMedio Diferencia { get; set; }

        public string Error { get; set; }

        /// <summary>Solo se registran los productos con alguna diferencia, aviso o error.</summary>
        [JsonIgnore]
        public bool DebeRegistrarse =>
            Clasificacion != ClasificacionSombraPrecioMedio.Igual &&
            Clasificacion != ClasificacionSombraPrecioMedio.SinFicha &&
            (Clasificacion != ClasificacionSombraPrecioMedio.NoProcesado || (Calculo?.Avisos.Any() ?? false));

        /// <summary>Diferencia que hay que investigar (no explicada por pendientes ni empates).</summary>
        [JsonIgnore]
        public bool EsNoEsperada => Clasificacion == ClasificacionSombraPrecioMedio.Distinto || Clasificacion == ClasificacionSombraPrecioMedio.Error;
    }

    /// <summary>Resumen de una pasada de la sombra para una empresa.</summary>
    public sealed class ResumenEmpresaSombraPrecioMedio
    {
        public string Empresa { get; set; }
        public string EmpresaEspejo { get; set; }
        public int Productos { get; set; }
        public int Revisados { get; set; }
        public int Iguales { get; set; }
        public int IgualesConAvisos { get; set; }
        public int Pendientes { get; set; }
        public int Empates { get; set; }
        public int Distintos { get; set; }
        public int NoProcesados { get; set; }
        public int SinFicha { get; set; }
        public int Errores { get; set; }
        public int VentasMuestreadas { get; set; }
        public int VentasComparadas { get; set; }
        public int VentasDistintas { get; set; }
        public double Segundos { get; set; }
        public bool Interrumpida { get; set; }

        /// <summary>Los primeros productos con diferencia no esperada (para el correo y el ELMAH).</summary>
        public List<ResultadoSombraPrecioMedio> PrimerosNoEsperados { get; } = new List<ResultadoSombraPrecioMedio>();

        public int NoEsperados => Distintos + Errores;
    }

    /// <summary>Resumen de una pasada completa.</summary>
    public sealed class ResumenPasadaSombraPrecioMedio
    {
        public DateTime FechaPasada { get; set; }
        public DateTime CorteSP { get; set; }
        public bool Registrada { get; set; }

        /// <summary>Error que ha impedido hacer la pasada (p. ej. falta la tabla). Nulo si ha ido bien.</summary>
        public string Error { get; set; }

        public List<ResumenEmpresaSombraPrecioMedio> Empresas { get; } = new List<ResumenEmpresaSombraPrecioMedio>();

        public int NoEsperados => Empresas.Sum(e => e.NoEsperados);

        public override string ToString()
        {
            if (Error != null)
            {
                return "Sombra de precios medios NO ejecutada: " + Error;
            }
            return string.Format(CultureInfo.InvariantCulture, "Sombra de precios medios {0:dd/MM/yyyy} (corte SP {1:dd/MM/yyyy HH:mm}): ", FechaPasada, CorteSP) +
                string.Join(" | ", Empresas.Select(e => string.Format(CultureInfo.InvariantCulture,
                    "empresa {0}: {1}/{2} productos en {3:0}s{4}; iguales {5}, con avisos {6}, pendientes {7}, empates {8}, DISTINTOS {9}, errores {10}, no procesados {11}; ventas {12} comparadas, {13} distintas",
                    e.Empresa, e.Revisados, e.Productos, e.Segundos, e.Interrumpida ? " (INTERRUMPIDA por tiempo)" : "",
                    e.Iguales, e.IgualesConAvisos, e.Pendientes, e.Empates, e.Distintos, e.Errores, e.NoProcesados,
                    e.VentasComparadas, e.VentasDistintas)));
        }
    }

    /// <summary>
    /// Sombra de los precios medios (Issue #547, corte b): calcula con <see cref="CalculadoraPrecioMedio"/> y
    /// COMPARA con lo que dejó el SP del domingo. NUNCA escribe en Productos, LinPedidoCmp ni LinPedidoVta: lo
    /// único que escribe (si se pide) es la tabla de diagnóstico <c>PreciosMediosSombra</c>.
    /// </summary>
    public class ServicioSombraPreciosMedios
    {
        /// <summary>Empresas que procesa el SP (la 2 y la 6 no; la 3 es la espejo de la 1).</summary>
        public static readonly IReadOnlyList<string> EMPRESAS = new[] { "1", "4", "5" };

        /// <summary>Productos por empresa a los que se les muestrean las ventas (plan §4: ~200).</summary>
        public const int MUESTRA_VENTAS_POR_EMPRESA = 200;

        /// <summary>Productos con diferencia no esperada que se guardan para el correo.</summary>
        public const int MAXIMO_NO_ESPERADOS_EN_RESUMEN = 20;

        private static readonly HashSet<TipoAvisoPrecioMedio> AVISOS_DE_EMPATE = new HashSet<TipoAvisoPrecioMedio>
        {
            TipoAvisoPrecioMedio.EmpateFacturasMismaFecha,
            TipoAvisoPrecioMedio.EmpateAlbaranAnterior,
            TipoAvisoPrecioMedio.AlbaranAnteriorConVariosCostes
        };

        private readonly IRepositorioPreciosMedios repositorio;

        public ServicioSombraPreciosMedios(IRepositorioPreciosMedios repositorio)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
        }

        /// <summary>
        /// Última ejecución del SP de los domingos, o nulo si no se puede saber (sin permiso en msdb, etc.).
        /// </summary>
        public EjecucionSPPreciosMedios UltimaEjecucionSP()
        {
            try
            {
                return repositorio.UltimaEjecucionSP();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Inicio de la última pasada del SP: lo que diga msdb y, si no se puede leer, el domingo más reciente a la
        /// hora programada (<paramref name="corteSiNoSeSabe"/>).
        /// </summary>
        public DateTime CorteSP(DateTime corteSiNoSeSabe)
        {
            return UltimaEjecucionSP()?.Inicio ?? corteSiNoSeSabe;
        }

        /// <summary>Sombra de un producto (no registra nada). Para el endpoint de diagnóstico.</summary>
        public ResultadoSombraPrecioMedio CompararProducto(string empresa, string producto, DateTime corteSP, bool conVentas)
        {
            string espejo = repositorio.EmpresaEspejo(empresa) ?? empresa;
            DatosProductoPrecioMedio datos = repositorio.LeerDatos(empresa, espejo, producto, conVentas);
            return Evaluar(datos, corteSP);
        }

        /// <summary>
        /// Calcula y clasifica un producto (puro). Primero con el historial actual; si difiere y hay facturas
        /// posteriores a la pasada del SP, repite con el historial que vio el SP: si entonces cuadra, es «pendiente».
        /// </summary>
        public static ResultadoSombraPrecioMedio Evaluar(DatosProductoPrecioMedio datos, DateTime corteSP)
        {
            ResultadoSombraPrecioMedio resultado = new ResultadoSombraPrecioMedio
            {
                Empresa = datos.Empresa?.Trim(),
                Producto = datos.Producto?.Trim()
            };
            if (datos.Ficha == null)
            {
                resultado.Clasificacion = ClasificacionSombraPrecioMedio.SinFicha;
                return resultado;
            }

            EstadoActualPrecioMedio bd = EstadoActualPrecioMedio.Desde(datos);
            ResultadoPrecioMedio calculoActual = CalculadoraPrecioMedio.Calcular(ConstructorHistorialPrecioMedio.Construir(datos));
            DiferenciaPrecioMedio diferenciaActual = ComparadorSombraPreciosMedios.Comparar(calculoActual, bd, corteSP);
            resultado.Calculo = calculoActual;
            resultado.Diferencia = diferenciaActual;

            if (!calculoActual.Procesado)
            {
                resultado.Clasificacion = ClasificacionSombraPrecioMedio.NoProcesado;
                return resultado;
            }
            if (!diferenciaActual.HayDiferencia)
            {
                resultado.Clasificacion = calculoActual.Avisos.Any()
                    ? ClasificacionSombraPrecioMedio.IgualConAvisos
                    : ClasificacionSombraPrecioMedio.Igual;
                return resultado;
            }

            HistorialPrecioMedio historialSP = ConstructorHistorialPrecioMedio.Construir(datos, corteSP, out int pendientes);
            resultado.LineasPendientes = pendientes;
            ResultadoPrecioMedio calculoSP = calculoActual;
            if (pendientes > 0)
            {
                calculoSP = CalculadoraPrecioMedio.Calcular(historialSP);
                DiferenciaPrecioMedio diferenciaSP = ComparadorSombraPreciosMedios.Comparar(calculoSP, bd, corteSP);
                resultado.Diferencia = diferenciaSP;
                if (!diferenciaSP.HayDiferencia)
                {
                    resultado.Clasificacion = ClasificacionSombraPrecioMedio.Pendiente;
                    return resultado;
                }
            }

            resultado.Clasificacion = calculoSP.Avisos.Any(a => AVISOS_DE_EMPATE.Contains(a.Tipo))
                ? ClasificacionSombraPrecioMedio.Empate
                : ClasificacionSombraPrecioMedio.Distinto;
            return resultado;
        }

        /// <summary>
        /// Pasada completa (o de los productos indicados) por las empresas indicadas. Si <paramref name="registrar"/>,
        /// deja en <c>PreciosMediosSombra</c> los productos con diferencia o aviso y una fila resumen por empresa
        /// (antes borra lo que hubiera de esa fecha y empresa: repetirla el mismo día es idempotente). Si la tabla no
        /// existe, no lee nada y devuelve el error en el resumen.
        /// </summary>
        public ResumenPasadaSombraPrecioMedio EjecutarPasada(IEnumerable<string> empresas, DateTime fechaPasada, DateTime corteSP,
            bool registrar, TimeSpan limite, int muestraVentasPorEmpresa = MUESTRA_VENTAS_POR_EMPRESA)
        {
            ResumenPasadaSombraPrecioMedio resumen = new ResumenPasadaSombraPrecioMedio
            {
                FechaPasada = fechaPasada.Date,
                CorteSP = corteSP,
                Registrada = registrar
            };

            if (registrar)
            {
                bool existe;
                try
                {
                    existe = repositorio.ExisteTablaSombra();
                }
                catch (Exception ex)
                {
                    resumen.Error = "No se ha podido comprobar la tabla PreciosMediosSombra: " + ex.Message;
                    return resumen;
                }
                if (!existe)
                {
                    resumen.Error = "No existe la tabla PreciosMediosSombra: falta ejecutar Scripts/Issue547_PreciosMediosSombra.sql";
                    return resumen;
                }
            }

            Stopwatch reloj = Stopwatch.StartNew();
            foreach (string empresa in empresas)
            {
                ResumenEmpresaSombraPrecioMedio resumenEmpresa = ProcesarEmpresa(empresa, resumen, registrar, reloj, limite, muestraVentasPorEmpresa);
                resumen.Empresas.Add(resumenEmpresa);
                if (registrar)
                {
                    GuardarResumen(resumen, resumenEmpresa);
                }
                if (reloj.Elapsed > limite)
                {
                    break;
                }
            }
            return resumen;
        }

        private ResumenEmpresaSombraPrecioMedio ProcesarEmpresa(string empresa, ResumenPasadaSombraPrecioMedio resumen, bool registrar,
            Stopwatch reloj, TimeSpan limite, int muestraVentas)
        {
            Stopwatch relojEmpresa = Stopwatch.StartNew();
            ResumenEmpresaSombraPrecioMedio r = new ResumenEmpresaSombraPrecioMedio { Empresa = empresa };
            string espejo = repositorio.EmpresaEspejo(empresa) ?? empresa;
            r.EmpresaEspejo = espejo;

            if (registrar)
            {
                repositorio.BorrarPasada(resumen.FechaPasada, empresa);
            }

            IReadOnlyList<string> productos = repositorio.ProductosConCompras(empresa, espejo);
            r.Productos = productos.Count;
            HashSet<string> conVentas = ElegirMuestraVentas(productos, muestraVentas, resumen.FechaPasada);

            foreach (string producto in productos)
            {
                if (reloj.Elapsed > limite)
                {
                    r.Interrumpida = true;
                    break;
                }
                ResultadoSombraPrecioMedio resultado;
                bool muestreado = conVentas.Contains(producto);
                try
                {
                    DatosProductoPrecioMedio datos = repositorio.LeerDatos(empresa, espejo, producto, muestreado);
                    resultado = Evaluar(datos, resumen.CorteSP);
                }
                catch (Exception ex)
                {
                    resultado = new ResultadoSombraPrecioMedio
                    {
                        Empresa = empresa,
                        Producto = producto,
                        Clasificacion = ClasificacionSombraPrecioMedio.Error,
                        Error = ex.Message
                    };
                }
                r.Revisados++;
                Contar(r, resultado, muestreado);

                if (registrar && resultado.DebeRegistrarse)
                {
                    try
                    {
                        repositorio.GuardarFila(CrearFila(resumen.FechaPasada, resultado));
                    }
                    catch (Exception ex)
                    {
                        r.Errores++;
                        resultado.Error = "No se ha podido registrar: " + ex.Message;
                    }
                }
                if (resultado.EsNoEsperada && r.PrimerosNoEsperados.Count < MAXIMO_NO_ESPERADOS_EN_RESUMEN)
                {
                    r.PrimerosNoEsperados.Add(resultado);
                }
            }
            r.Segundos = relojEmpresa.Elapsed.TotalSeconds;
            return r;
        }

        private static void Contar(ResumenEmpresaSombraPrecioMedio r, ResultadoSombraPrecioMedio resultado, bool muestreado)
        {
            switch (resultado.Clasificacion)
            {
                case ClasificacionSombraPrecioMedio.Igual: r.Iguales++; break;
                case ClasificacionSombraPrecioMedio.IgualConAvisos: r.IgualesConAvisos++; break;
                case ClasificacionSombraPrecioMedio.Pendiente: r.Pendientes++; break;
                case ClasificacionSombraPrecioMedio.Empate: r.Empates++; break;
                case ClasificacionSombraPrecioMedio.Distinto: r.Distintos++; break;
                case ClasificacionSombraPrecioMedio.NoProcesado: r.NoProcesados++; break;
                case ClasificacionSombraPrecioMedio.SinFicha: r.SinFicha++; break;
                case ClasificacionSombraPrecioMedio.Error: r.Errores++; break;
            }
            if (muestreado)
            {
                r.VentasMuestreadas++;
                r.VentasComparadas += resultado.Diferencia?.VentasComparadas ?? 0;
                r.VentasDistintas += resultado.Diferencia?.VentasDistintas ?? 0;
            }
        }

        /// <summary>
        /// Productos a los que se les leen las ventas: una muestra que cambia cada semana (semilla = fecha de la
        /// pasada), para que con las semanas se vayan viendo todos sin leer nunca los 2,7 M de líneas de golpe.
        /// </summary>
        internal static HashSet<string> ElegirMuestraVentas(IReadOnlyList<string> productos, int tamano, DateTime fechaPasada)
        {
            if (tamano <= 0 || productos.Count == 0)
            {
                return new HashSet<string>();
            }
            if (tamano >= productos.Count)
            {
                return new HashSet<string>(productos);
            }
            Random aleatorio = new Random(fechaPasada.Date.GetHashCode());
            return new HashSet<string>(productos.OrderBy(p => aleatorio.Next()).Take(tamano));
        }

        internal static FilaPreciosMediosSombra CrearFila(DateTime fechaPasada, ResultadoSombraPrecioMedio resultado)
        {
            DiferenciaPrecioMedio d = resultado.Diferencia;
            List<AvisoPrecioMedio> avisos = resultado.Calculo?.Avisos ?? new List<AvisoPrecioMedio>();
            return new FilaPreciosMediosSombra
            {
                FechaPasada = fechaPasada.Date,
                Empresa = resultado.Empresa,
                Producto = resultado.Producto,
                EsResumen = false,
                Clasificacion = resultado.Clasificacion.ToString(),
                PrecioMedioBD = d?.PrecioMedioBD,
                PrecioMedioCalculado = d?.PrecioMedioCalculado,
                LineasComparadas = d?.LineasComparadas ?? 0,
                LineasDistintas = d?.LineasDistintas ?? 0,
                PrimeraLineaDistinta = d?.PrimeraLineaDistinta,
                LineasPendientes = resultado.LineasPendientes,
                VentasComparadas = d?.VentasComparadas,
                VentasDistintas = d?.VentasDistintas,
                Avisos = avisos.Any() ? string.Join(Environment.NewLine, avisos.Select(a => a.ToString())) : null,
                Detalle = JsonConvert.SerializeObject(new
                {
                    resultado.Error,
                    Motivo = resultado.Calculo?.Motivo.ToString(),
                    MediaDistinta = d?.MediaDistinta,
                    d?.VentasConEmpateDeTramos,
                    d?.VentasPosterioresAlCorte,
                    Diferencias = d?.Detalles
                })
            };
        }

        private void GuardarResumen(ResumenPasadaSombraPrecioMedio resumen, ResumenEmpresaSombraPrecioMedio e)
        {
            try
            {
                repositorio.GuardarFila(new FilaPreciosMediosSombra
                {
                    FechaPasada = resumen.FechaPasada,
                    Empresa = e.Empresa,
                    Producto = FilaPreciosMediosSombra.PRODUCTO_RESUMEN,
                    EsResumen = true,
                    Clasificacion = e.NoEsperados > 0 ? ClasificacionSombraPrecioMedio.Distinto.ToString() : ClasificacionSombraPrecioMedio.Igual.ToString(),
                    LineasComparadas = e.Revisados,
                    LineasDistintas = e.Distintos,
                    LineasPendientes = e.Pendientes,
                    VentasComparadas = e.VentasComparadas,
                    VentasDistintas = e.VentasDistintas,
                    Detalle = JsonConvert.SerializeObject(new
                    {
                        resumen.CorteSP,
                        e.EmpresaEspejo,
                        e.Productos,
                        e.Revisados,
                        e.Iguales,
                        e.IgualesConAvisos,
                        e.Pendientes,
                        e.Empates,
                        e.Distintos,
                        e.NoProcesados,
                        e.SinFicha,
                        e.Errores,
                        e.VentasMuestreadas,
                        e.VentasComparadas,
                        e.VentasDistintas,
                        Segundos = Math.Round(e.Segundos, 1),
                        e.Interrumpida,
                        PrimerosNoEsperados = e.PrimerosNoEsperados.Select(p => p.Producto).ToList()
                    })
                });
            }
            catch (Exception ex)
            {
                e.Errores++;
                ElmahHelper.Log(new Exception("[Precios medios #547] No se ha podido guardar el resumen de la sombra de la empresa " +
                    e.Empresa + ": " + ex.Message, ex), "Sistema (sombra de precios medios)");
            }
        }
    }
}
