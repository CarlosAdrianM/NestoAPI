using NestoAPI.Infraestructure.PedidosVenta.CondicionesCampana;
using NestoAPI.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Campanas
{
    /// <summary>Respuesta de POST api/Campanas/Bloque (ensayo y de verdad).</summary>
    public class RespuestaCampanaBloqueDTO
    {
        public string Nombre { get; set; }
        public string Empresa { get; set; }
        public DateTime FechaDesde { get; set; }
        public DateTime FechaHasta { get; set; }
        /// <summary>True: no se ha escrito nada.</summary>
        public bool Ensayo { get; set; }
        /// <summary>Si la campaña ya está corriendo hoy.</summary>
        public bool Vigente { get; set; }
        public List<TotalesCampanaBloqueDTO> PorFamilia { get; set; } = new List<TotalesCampanaBloqueDTO>();
        public TotalesCampanaBloqueDTO Total { get; set; }
        /// <summary>Hasta <see cref="ServicioCampanaBloque.MUESTRA_POR_FAMILIA"/> líneas de cada familia (primero las que entran).</summary>
        public List<LineaCampanaBloqueDTO> Muestra { get; set; } = new List<LineaCampanaBloqueDTO>();
        /// <summary>Todas las líneas, solo con <c>incluirLineas=true</c> (la lista completa va mejor en POST api/Campanas/Bloque/Excel).</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<LineaCampanaBloqueDTO> Lineas { get; set; }
        public List<string> Avisos { get; set; } = new List<string>();
        /// <summary>De verdad: filas de DescuentosProducto creadas (las que «ya estaban» no se vuelven a crear).</summary>
        public int FilasCreadas { get; set; }
        /// <summary>De verdad: filas nuevas en dbo.CampanasAplicarDto.</summary>
        public int FichasDesbloqueoCreadas { get; set; }
        /// <summary>De verdad: filas nuevas en dbo.CampanasCondicionContado.</summary>
        public int CondicionContadoCreadas { get; set; }
        /// <summary>De verdad: se ha marcado la campaña en dbo.CampanasAcumulanDistribuidores (false si ya lo estaba o no se pidió).</summary>
        public bool MarcaDistribuidoresCreada { get; set; }
        /// <summary>Productos encolados para la tienda (solo si la campaña ya está corriendo; si no, lo hace el job el día que empieza).</summary>
        public int ProductosEncolados { get; set; }
        public string Mensaje { get; set; }
    }

    public enum EstadoCampanaBloque
    {
        Ok,
        PeticionIncorrecta,
        /// <summary>Falta dbo.CampanasAplicarDto o dbo.CampanasCondicionContado y la petición las necesita.</summary>
        SinTabla
    }

    public class ResultadoCampanaBloque
    {
        public EstadoCampanaBloque Estado { get; set; }
        public string Mensaje { get; set; }
        public RespuestaCampanaBloqueDTO Respuesta { get; set; }
    }

    /// <summary>Lo que hay que leer (familias de las reglas y productos de las excepciones).</summary>
    public class ConsultaCampanaBloque
    {
        public string Empresa { get; set; }
        public string Campana { get; set; }
        public DateTime Desde { get; set; }
        public DateTime Hasta { get; set; }
        public List<string> Familias { get; set; } = new List<string>();
        public List<string> Productos { get; set; } = new List<string>();
    }

    public class DatosCampanaBloque
    {
        public List<ProductoCandidatoCampana> Candidatos { get; set; } = new List<ProductoCandidatoCampana>();
        public List<FilaExistenteCampana> Existentes { get; set; } = new List<FilaExistenteCampana>();
        public bool TablaAplicarDto { get; set; }
        public bool TablaCondicionContado { get; set; }
        public bool TablaAcumulaDistribuidores { get; set; }
    }

    public class FilaAltaCampanaBloque
    {
        public string Producto { get; set; }
        public decimal Descuento { get; set; }
        public byte Audiencia { get; set; }
        /// <summary>False en las que ya estaban: solo se completan sus filas de desbloqueo y contado si faltan.</summary>
        public bool CrearDescuento { get; set; }
        public bool Desbloquear { get; set; }
        public bool Contado { get; set; }
    }

    public class AltaCampanaBloque
    {
        public string Empresa { get; set; }
        public string Campana { get; set; }
        public DateTime Desde { get; set; }
        public DateTime Hasta { get; set; }
        public string Usuario { get; set; }
        public string TextoAvisoContado { get; set; }
        public bool AcumulaParaDistribuidores { get; set; }
        public List<FilaAltaCampanaBloque> Filas { get; set; } = new List<FilaAltaCampanaBloque>();
    }

    public class ResultadoInsercionCampanaBloque
    {
        /// <summary>Los productos cuya fila de DescuentosProducto se ha creado de verdad.</summary>
        public List<string> ProductosCreados { get; set; } = new List<string>();
        public int AplicarDtoCreadas { get; set; }
        public int CondicionContadoCreadas { get; set; }
        public bool MarcaDistribuidoresCreada { get; set; }
    }

    /// <summary>Borrar (sin <see cref="FechaFin"/>) o cerrar una campaña entera por nombre.</summary>
    public class RetiradaCampanaBloque
    {
        public string Empresa { get; set; }
        public string Campana { get; set; }
        /// <summary>Nº Orden de sus filas de DescuentosProducto (las que ha encontrado el mantenimiento de campañas).</summary>
        public List<int> Ids { get; set; } = new List<int>();
        /// <summary>Null = borrar; con fecha = cerrar (FechaHasta).</summary>
        public DateTime? FechaFin { get; set; }
        public DateTime Hoy { get; set; }
        public string Usuario { get; set; }
    }

    public class ResultadoRetiradaCampanaBloque
    {
        public int FilasDescuento { get; set; }
        /// <summary>dbo.CampanasAplicarDto: borradas (aún sin activar) o con la fecha de fin cambiada (el job devuelve la ficha).</summary>
        public int FilasAplicarDto { get; set; }
        public int FilasCondicionContado { get; set; }
        /// <summary>dbo.CampanasAcumulanDistribuidores: la marca borrada o cerrada.</summary>
        public int FilasAcumulaDistribuidores { get; set; }
    }

    public interface IRepositorioCampanaBloque
    {
        /// <summary>Lectura sin transacción (ensayo).</summary>
        Task<DatosCampanaBloque> Leer(ConsultaCampanaBloque consulta);
        /// <summary>Abre la transacción con el bloqueo «CampanaBloque:{empresa}:{campana}».</summary>
        Task<IUnidadCampanaBloque> Empezar(string empresa, string campana);
        /// <summary>Borra o cierra la campaña entera (DescuentosProducto, CampanasAplicarDto y CampanasCondicionContado) en UNA transacción, con su rastro.</summary>
        Task<ResultadoRetiradaCampanaBloque> Retirar(RetiradaCampanaBloque retirada);
        Task<int> Encolar(IEnumerable<string> productos, string usuario);
    }

    /// <summary>Sin <see cref="Confirmar"/>, el Dispose lo deshace.</summary>
    public interface IUnidadCampanaBloque : IDisposable
    {
        Task<DatosCampanaBloque> Leer(ConsultaCampanaBloque consulta);
        Task<ResultadoInsercionCampanaBloque> Insertar(AltaCampanaBloque alta, DatosCampanaBloque tablas);
        Task Anotar(string tabla, string anterior, string nuevo, string usuario);
        void Confirmar();
    }

    /// <summary>
    /// El alta en bloque de una campaña: valida, lee en una consulta los candidatos y las filas que ya hay, aplica
    /// <see cref="ReglaCampanaBloque"/> y, si no es ensayo, lo escribe todo en UNA transacción por lotes con UNA fila de rastro
    /// en Modificaciones (Tabla <see cref="TABLA_MODIFICACIONES"/>). Idempotente: lo que ya tiene su fila de la campaña se
    /// cuenta como «ya estaba». Solo encola en la tienda si la campaña ya está corriendo (si no, el job vigencia-campanas lo
    /// hace el día que empieza): y entonces una vez por producto.
    /// </summary>
    public class ServicioCampanaBloque
    {
        public const string TABLA_MODIFICACIONES = "CampanaBloque";
        public const string TABLA_BORRADA = "CampanaBorrada";
        public const string TABLA_CERRADA = "CampanaCerrada";
        public const int MUESTRA_POR_FAMILIA = 20;
        internal const string USUARIO_ENCOLADO = "Campaña en bloque";

        private readonly IRepositorioCampanaBloque repositorio;
        private readonly Action<Exception> registrar;

        public ServicioCampanaBloque(IRepositorioCampanaBloque repositorio, Action<Exception> registrar = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.registrar = registrar ?? (ex => ElmahHelper.Log(ex));
        }

        public async Task<ResultadoCampanaBloque> Ejecutar(PeticionCampanaBloqueDTO peticion, bool ensayo, bool incluirLineas, DateTime hoy, string usuario)
        {
            string error = ReglaCampanaBloque.Validar(peticion, hoy);
            if (error != null)
            {
                return new ResultadoCampanaBloque { Estado = EstadoCampanaBloque.PeticionIncorrecta, Mensaje = error };
            }
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO;
            string campana = peticion.Nombre.Trim();
            var consulta = new ConsultaCampanaBloque
            {
                Empresa = empresa,
                Campana = campana,
                Desde = peticion.FechaDesde.Value.Date,
                Hasta = peticion.FechaHasta.Value.Date,
                Familias = ReglaCampanaBloque.Familias(peticion),
                Productos = ReglaCampanaBloque.ProductosExcepcion(peticion)
            };

            if (ensayo)
            {
                DatosCampanaBloque datos = await repositorio.Leer(consulta).ConfigureAwait(false) ?? new DatosCampanaBloque();
                RespuestaCampanaBloqueDTO respuesta = Responder(peticion, consulta, datos, true, incluirLineas, hoy, out _);
                respuesta.Avisos.AddRange(AvisosTablas(TablasQueFaltan(peticion, respuesta, datos)));
                respuesta.Mensaje = "Ensayo: no se ha escrito nada.";
                return new ResultadoCampanaBloque { Estado = EstadoCampanaBloque.Ok, Respuesta = respuesta };
            }

            string usuarioAuditoria = UsuarioAuditoriaHelper.ParaAuditoria(usuario);
            RespuestaCampanaBloqueDTO hecha;
            ResultadoInsercionCampanaBloque insercion;
            List<LineaCampanaBloqueDTO> lineas;
            using (IUnidadCampanaBloque unidad = await repositorio.Empezar(empresa, campana).ConfigureAwait(false))
            {
                DatosCampanaBloque datos = await unidad.Leer(consulta).ConfigureAwait(false) ?? new DatosCampanaBloque();
                hecha = Responder(peticion, consulta, datos, false, incluirLineas, hoy, out lineas);
                List<string> faltan = TablasQueFaltan(peticion, hecha, datos);
                if (faltan.Any())
                {
                    return new ResultadoCampanaBloque { Estado = EstadoCampanaBloque.SinTabla, Mensaje = string.Join(" ", AvisosTablas(faltan)) };
                }
                AltaCampanaBloque alta = Alta(peticion, consulta, usuarioAuditoria, lineas);
                insercion = alta.Filas.Any() || alta.AcumulaParaDistribuidores
                    ? await unidad.Insertar(alta, datos).ConfigureAwait(false) ?? new ResultadoInsercionCampanaBloque()
                    : new ResultadoInsercionCampanaBloque();
                hecha.FilasCreadas = insercion.ProductosCreados.Count;
                hecha.FichasDesbloqueoCreadas = insercion.AplicarDtoCreadas;
                hecha.CondicionContadoCreadas = insercion.CondicionContadoCreadas;
                hecha.MarcaDistribuidoresCreada = insercion.MarcaDistribuidoresCreada;
                if (hecha.FilasCreadas + hecha.FichasDesbloqueoCreadas + hecha.CondicionContadoCreadas > 0 || hecha.MarcaDistribuidoresCreada)
                {
                    await unidad.Anotar(TABLA_MODIFICACIONES, RastroAlta(peticion, hecha), DescripcionAlta(hecha, usuarioAuditoria), usuarioAuditoria).ConfigureAwait(false);
                }
                unidad.Confirmar();
            }

            if (hecha.CondicionContadoCreadas > 0)
            {
                CampanasCondicionContado.Invalidar();
            }
            if (hecha.MarcaDistribuidoresCreada)
            {
                CampanasAcumulanDistribuidores.Invalidar();
            }
            hecha.ProductosEncolados = await EncolarSiYaCorre(hecha, insercion, lineas).ConfigureAwait(false);
            hecha.Mensaje = MensajeAlta(hecha);
            return new ResultadoCampanaBloque { Estado = EstadoCampanaBloque.Ok, Respuesta = hecha };
        }

        private static RespuestaCampanaBloqueDTO Responder(PeticionCampanaBloqueDTO peticion, ConsultaCampanaBloque consulta, DatosCampanaBloque datos,
            bool ensayo, bool incluirLineas, DateTime hoy, out List<LineaCampanaBloqueDTO> lineas)
        {
            ResultadoCalculoCampanaBloque calculo = ReglaCampanaBloque.Calcular(peticion, datos.Candidatos, datos.Existentes);
            lineas = calculo.Lineas;
            (List<TotalesCampanaBloqueDTO> porFamilia, TotalesCampanaBloqueDTO total) = ReglaCampanaBloque.Totales(lineas);
            return new RespuestaCampanaBloqueDTO
            {
                Nombre = consulta.Campana,
                Empresa = consulta.Empresa,
                FechaDesde = consulta.Desde,
                FechaHasta = consulta.Hasta,
                Ensayo = ensayo,
                Vigente = Vigencia.EsVigente(consulta.Desde, consulta.Hasta, hoy.Date, 0),
                PorFamilia = porFamilia,
                Total = total,
                Muestra = ReglaCampanaBloque.Muestra(lineas, MUESTRA_POR_FAMILIA),
                Lineas = incluirLineas ? lineas : null,
                Avisos = calculo.Avisos
            };
        }

        private static List<string> TablasQueFaltan(PeticionCampanaBloqueDTO p, RespuestaCampanaBloqueDTO r, DatosCampanaBloque datos)
        {
            var faltan = new List<string>();
            if (p.AcumulaParaDistribuidores && !datos.TablaAcumulaDistribuidores)
            {
                faltan.Add("CampanasAcumulanDistribuidores");
            }
            if (r.Total.FichasDesbloqueadas > 0 && !datos.TablaAplicarDto)
            {
                faltan.Add("CampanasAplicarDto");
            }
            if (r.Total.CondicionContado > 0 && !datos.TablaCondicionContado)
            {
                faltan.Add("CampanasCondicionContado");
            }
            return faltan;
        }

        private static IEnumerable<string> AvisosTablas(IEnumerable<string> faltan) =>
            faltan.Select(t => t == "CampanasAcumulanDistribuidores"
                ? "Falta la tabla dbo.CampanasAcumulanDistribuidores: lance Scripts/BlackFriday2026_CampanasAcumulanDistribuidores.sql antes de crearla de verdad."
                : $"Falta la tabla dbo.{t}: lance Scripts/FeriaOnline_CampanasCondicionContado.sql y Scripts/BlackFriday2026_CampanasBloque.sql antes de crearla de verdad.");

        private static AltaCampanaBloque Alta(PeticionCampanaBloqueDTO peticion, ConsultaCampanaBloque consulta, string usuario, List<LineaCampanaBloqueDTO> lineas)
        {
            return new AltaCampanaBloque
            {
                Empresa = consulta.Empresa,
                Campana = consulta.Campana,
                Desde = consulta.Desde,
                Hasta = consulta.Hasta,
                Usuario = usuario,
                TextoAvisoContado = string.IsNullOrWhiteSpace(peticion.TextoAvisoContado) ? null : peticion.TextoAvisoContado.Trim(),
                AcumulaParaDistribuidores = peticion.AcumulaParaDistribuidores,
                Filas = lineas
                    .Where(l => l.Estado == EstadoLineaCampanaBloque.Entra
                        || (l.Estado == EstadoLineaCampanaBloque.YaEstaba && (l.DesbloquearFicha || l.CondicionContado)))
                    .GroupBy(l => l.Producto, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .Select(l => new FilaAltaCampanaBloque
                    {
                        Producto = l.Producto,
                        Descuento = l.Descuento,
                        Audiencia = l.Audiencia,
                        CrearDescuento = l.Estado == EstadoLineaCampanaBloque.Entra,
                        Desbloquear = l.DesbloquearFicha,
                        Contado = l.CondicionContado
                    })
                    .ToList()
            };
        }

        /// <summary>
        /// LA REGLA de la tienda al dar de alta: una campaña que aún no ha empezado no cambia ningún mensaje (la tienda solo
        /// publica lo vigente) y la publica el job vigencia-campanas el día que empieza. Si ya corre, se encolan una vez los
        /// productos cuya fila se acaba de crear y que viajan (audiencia &gt; 0).
        /// </summary>
        internal static List<string> ProductosAEncolarAlCrear(RespuestaCampanaBloqueDTO r, ResultadoInsercionCampanaBloque insercion, IEnumerable<LineaCampanaBloqueDTO> lineas)
        {
            if (r == null || !r.Vigente || insercion == null)
            {
                return new List<string>();
            }
            var creados = new HashSet<string>(insercion.ProductosCreados.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()), StringComparer.OrdinalIgnoreCase);
            return (lineas ?? Enumerable.Empty<LineaCampanaBloqueDTO>())
                .Where(l => l.Estado == EstadoLineaCampanaBloque.Entra && l.Audiencia > 0 && creados.Contains(l.Producto))
                .Select(l => l.Producto)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private async Task<int> EncolarSiYaCorre(RespuestaCampanaBloqueDTO r, ResultadoInsercionCampanaBloque insercion, List<LineaCampanaBloqueDTO> lineas)
        {
            List<string> productos = ProductosAEncolarAlCrear(r, insercion, lineas);
            if (!productos.Any())
            {
                return 0;
            }
            try
            {
                _ = await repositorio.Encolar(productos, USUARIO_ENCOLADO).ConfigureAwait(false);
                return productos.Count;
            }
            catch (Exception ex)
            {
                registrar(new Exception($"[Campaña en bloque] Campaña «{r.Nombre}» creada, pero no se han podido encolar {productos.Count} productos para la tienda: {ex.Message}", ex));
                r.Avisos.Add("La campaña se ha creado, pero no se han podido mandar los productos a la tienda: se publicarán en la sincronización nocturna.");
                return 0;
            }
        }

        internal static string MensajeAlta(RespuestaCampanaBloqueDTO r)
        {
            string fechas = $"del {r.FechaDesde.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)} al {r.FechaHasta.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}";
            string tienda = r.Vigente
                ? $"Ya está corriendo: {r.ProductosEncolados.ToString("N0", CultureInfo.GetCultureInfo("es-ES"))} productos enviados a la tienda."
                : $"La tienda la publicará el {r.FechaDesde.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}, cuando empiece.";
            return $"Campaña «{r.Nombre}» {fechas}: {N(r.FilasCreadas)} filas creadas, {N(r.Total.YaEstaban)} ya estaban, {N(r.Total.Fuera)} productos fuera. {tienda}";
        }

        internal static string DescripcionAlta(RespuestaCampanaBloqueDTO r, string usuario) =>
            $"Campaña «{r.Nombre}» ({r.FechaDesde.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}-{r.FechaHasta.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}) " +
            $"dada de alta en bloque: {N(r.FilasCreadas)} filas nuevas, {N(r.Total.YaEstaban)} ya estaban, {N(r.Total.Fuera)} fuera " +
            $"({N(r.Total.Solapes)} con solape), {N(r.FichasDesbloqueoCreadas)} fichas a desbloquear, {N(r.CondicionContadoCreadas)} con condición de contado" +
            (r.MarcaDistribuidoresCreada ? ", y a los distribuidores se les suma encima del suyo" : string.Empty) + $". Por {usuario}";

        private static string RastroAlta(PeticionCampanaBloqueDTO p, RespuestaCampanaBloqueDTO r) =>
            JsonConvert.SerializeObject(new
            {
                Campana = r.Nombre,
                r.Empresa,
                FechaDesde = r.FechaDesde.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                FechaHasta = r.FechaHasta.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                p.Audiencia,
                p.DesbloquearAplicarDto,
                p.CondicionContado,
                p.AcumulaParaDistribuidores,
                Reglas = (p.Reglas ?? new List<ReglaCampanaBloqueDTO>()).Select(x => new
                {
                    Familia = x.Familia?.Trim(), x.Descuento, x.Audiencia, x.CondicionContado,
                    x.ProductosExcluidos, x.TextosExcluidos, x.GruposExcluidos, x.SubgruposExcluidos
                }),
                Excepciones = (p.Excepciones ?? new List<ExcepcionCampanaBloqueDTO>()).Select(x => new { Producto = x.Producto?.Trim(), x.Descuento, x.Audiencia, x.CondicionContado }),
                p.ProductosExcluidos,
                r.FilasCreadas,
                r.FichasDesbloqueoCreadas,
                r.CondicionContadoCreadas,
                r.Total,
                r.PorFamilia
            });

        /// <summary>«Campaña «Black Friday 2026» borrada: 6.512 filas de descuento, 120 de desbloqueo de ficha y 0 de contado. Por …».</summary>
        public static string DescripcionRetirada(RetiradaCampanaBloque r, ResultadoRetiradaCampanaBloque hecho) =>
            $"Campaña «{r.Campana}» " +
            (r.FechaFin.HasValue ? $"cerrada con fin el {r.FechaFin.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}" : "borrada") +
            $": {N(hecho.FilasDescuento)} filas de descuento, {N(hecho.FilasAplicarDto)} de desbloqueo de ficha y {N(hecho.FilasCondicionContado)} de contado" +
            (hecho.FilasAcumulaDistribuidores > 0 ? " (y su marca de distribuidores)" : string.Empty) + $". Por {r.Usuario}";

        private static string N(int n) => n.ToString("N0", CultureInfo.GetCultureInfo("es-ES"));
    }
}
