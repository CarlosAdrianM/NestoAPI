using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Sincronizacion;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#423 (Slice 5): mantenimiento de las campañas comerciales, para que dejen de vivir
    /// en las reglas de catálogo de PrestaShop y para que meterlas no sea teclear INSERTs a mano
    /// en DescuentosProducto — que es como acabamos el 31/08/2026 borrando 2.017 filas de las
    /// rebajas de verano a pelo.
    ///
    /// Una campaña ES una fila de DescuentosProducto de TARIFA (sin cliente ni proveedor), no una
    /// tabla nueva: así el descuento que anuncia la tienda y el que cobra el pedido salen del
    /// mismo sitio por construcción, que es el objetivo de #423. Lo que la distingue de un
    /// descuento de siempre es que lleva fechas, audiencia, o las dos cosas.
    ///
    /// Los tres niveles admitidos son los tres que el motor de precios aplica de verdad en tarifa:
    /// producto, familia y familia+grupo. Una fila solo con grupo NO es un nivel del motor y se
    /// rechaza: publicarla anunciaría un descuento que Nesto no cobra.
    ///
    /// Al guardar o borrar se encolan en Nesto_sync los productos alcanzados, para que la tienda
    /// se entere en la siguiente pasada de los 5 minutos y no haya que esperar al job nocturno
    /// (que es solo para las transiciones POR FECHA, que no tocan ninguna fila).
    /// </summary>
    [Authorize]
    public class CampanasController : ApiController
    {
        private readonly NVEntities db;

        public CampanasController()
        {
            db = new NVEntities();
        }

        public CampanasController(NVEntities db)
        {
            this.db = db;
        }

        /// <summary>
        /// TODAS las filas de tarifa, no solo las que "parecen" campaña.
        ///
        /// La primera versión devolvía únicamente las que llevaban fechas o audiencia, con la idea
        /// de no llenar la pantalla de los descuentos de siempre. Estaba mal: los descuentos que
        /// hay que mantener HOY —las 2.016 filas de las rebajas de verano de 2026, por ejemplo—
        /// son justamente las que tienen AudienciaOferta 0 y ninguna fecha, porque se metieron
        /// antes de que existiera el concepto de campaña. Ocultarlas dejaba fuera de la pantalla
        /// precisamente lo que hay que borrar, y obligaba a seguir haciéndolo por SQL: lo contrario
        /// de para lo que se hace #423.
        ///
        /// El filtro sigue estando para cuando la lista sea larga, pero es opt-in y NO es el
        /// comportamiento por defecto: una pantalla de mantenimiento que esconde datos sin decirlo
        /// hace que uno crea que no están.
        /// </summary>
        /// <param name="incluirCaducadas">Las que ya pasaron su FechaHasta.</param>
        /// <param name="soloCampanas">Solo las que llevan fechas o audiencia (esconde los descuentos de siempre).</param>
        [HttpGet]
        [Route("api/Campanas")]
        [ResponseType(typeof(List<CampanaDTO>))]
        public async Task<IHttpActionResult> GetCampanas(bool incluirCaducadas = false, bool soloCampanas = false)
        {
            DateTime hoy = DateTime.Today;

            List<DescuentosProducto> filas = await FilasDeTarifa().ToListAsync().ConfigureAwait(false);

            if (soloCampanas)
            {
                filas = filas.Where(f => f.AudienciaOferta > 0 || f.FechaDesde != null || f.FechaHasta != null).ToList();
            }
            if (!incluirCaducadas)
            {
                filas = filas.Where(f => f.FechaHasta == null || f.FechaHasta >= hoy).ToList();
            }

            return Ok(filas.Select(f => ADto(f, hoy)).OrderBy(c => c.Familia).ThenBy(c => c.Producto).ToList());
        }

        [HttpPost]
        [Route("api/Campanas")]
        [ResponseType(typeof(CampanaDTO))]
        public async Task<IHttpActionResult> PostCampana([FromBody] CampanaDTO campana)
        {
            string error = await Validar(campana, null).ConfigureAwait(false);
            if (error != null)
            {
                return BadRequest(error);
            }

            string usuario = UsuarioAuditoriaHelper.Resolver(User, null);
            DescuentosProducto fila = new DescuentosProducto
            {
                Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                Nº_Producto = Vacio(campana.Producto) ? null : campana.Producto.Trim(),
                Familia = Vacio(campana.Familia) ? null : campana.Familia.Trim(),
                GrupoProducto = Vacio(campana.Grupo) ? null : campana.Grupo.Trim(),
                CantidadMínima = 1,
                Descuento = campana.Descuento,
                DescuentoPublico = campana.DescuentoPublico,
                AudienciaOferta = campana.AudienciaOferta,
                FechaDesde = campana.FechaDesde,
                FechaHasta = campana.FechaHasta,
                Campana = Vacio(campana.Campana) ? null : campana.Campana.Trim(),
                Usuario = usuario,
                Fecha_Modificación = DateTime.Now
            };

            _ = db.DescuentosProductoes.Add(fila);
            _ = await db.SaveChangesAsync().ConfigureAwait(false);

            await Republicar(fila, usuario).ConfigureAwait(false);

            return Ok(ADto(fila, DateTime.Today));
        }

        [HttpPut]
        [Route("api/Campanas/{id:int}")]
        public async Task<IHttpActionResult> PutCampana(int id, [FromBody] CampanaDTO campana)
        {
            DescuentosProducto fila = await BuscarDeTarifa(id).ConfigureAwait(false);
            if (fila == null)
            {
                return NotFound();
            }

            string error = await Validar(campana, id).ConfigureAwait(false);
            if (error != null)
            {
                return BadRequest(error);
            }

            string usuario = UsuarioAuditoriaHelper.Resolver(User, null);

            // Se republica el alcance de ANTES y el de DESPUÉS: si la campaña se mueve de una
            // familia a otra, o de familia a producto, los que dejan de estar alcanzados también
            // tienen que enterarse — si no, se quedan con la oferta puesta en la tienda.
            List<string> alcanceAnterior = await ProductosQueCambianDeMensaje(
                new List<DescuentosProducto> { Copia(fila) }).ConfigureAwait(false);

            fila.Nº_Producto = Vacio(campana.Producto) ? null : campana.Producto.Trim();
            fila.Familia = Vacio(campana.Familia) ? null : campana.Familia.Trim();
            fila.GrupoProducto = Vacio(campana.Grupo) ? null : campana.Grupo.Trim();
            fila.Descuento = campana.Descuento;
            fila.DescuentoPublico = campana.DescuentoPublico;
            fila.AudienciaOferta = campana.AudienciaOferta;
            fila.FechaDesde = campana.FechaDesde;
            fila.FechaHasta = campana.FechaHasta;
            fila.Campana = Vacio(campana.Campana) ? null : campana.Campana.Trim();
            fila.Usuario = usuario;
            fila.Fecha_Modificación = DateTime.Now;

            _ = await db.SaveChangesAsync().ConfigureAwait(false);

            await Republicar(fila, usuario, alcanceAnterior).ConfigureAwait(false);

            return Ok(ADto(fila, DateTime.Today));
        }

        /// <summary>
        /// Borra la campaña Y republica lo que alcanzaba. Sin esa republicación pasaría lo del
        /// DELETE a mano del 31/08: la fila desaparece sin dejar rastro que ningún disparador
        /// pueda detectar, y la tienda se queda anunciando la oferta para siempre.
        /// </summary>
        [HttpDelete]
        [Route("api/Campanas/{id:int}")]
        public async Task<IHttpActionResult> DeleteCampana(int id)
        {
            DescuentosProducto fila = await BuscarDeTarifa(id).ConfigureAwait(false);
            if (fila == null)
            {
                return NotFound();
            }

            string usuario = UsuarioAuditoriaHelper.Resolver(User, null);
            List<string> alcance = await ProductosQueCambianDeMensaje(
                new List<DescuentosProducto> { Copia(fila) }).ConfigureAwait(false);

            _ = db.DescuentosProductoes.Remove(fila);
            _ = await db.SaveChangesAsync().ConfigureAwait(false);

            foreach (string producto in alcance)
            {
                _ = await db.EncolarProductoSync(producto, usuario).ConfigureAwait(false);
            }

            return Ok();
        }

        /// <summary>
        /// Las campañas que existen, con su recuento y sus fechas. Es lo que llena el filtro de la
        /// pantalla y lo que hay que mirar ANTES de operar en bloque: nadie debería borrar 2.017
        /// filas sin ver antes el número.
        /// </summary>
        [HttpGet]
        [Route("api/Campanas/Nombres")]
        [ResponseType(typeof(List<ResumenCampanaDTO>))]
        public async Task<IHttpActionResult> GetNombresDeCampana()
        {
            DateTime hoy = DateTime.Today;

            List<DescuentosProducto> filas = await FilasDeTarifa()
                .Where(d => d.Campana != null)
                .ToListAsync().ConfigureAwait(false);

            List<ResumenCampanaDTO> resumen = filas
                .GroupBy(f => f.Campana.Trim())
                .Select(g => new ResumenCampanaDTO
                {
                    Campana = g.Key,
                    Filas = g.Count(),
                    // Las que de verdad se anuncian en la tienda. Puede ser 0 y no es un error:
                    // las rebajas de verano de 2026 son 2.017 filas y ninguna viaja.
                    FilasQueViajan = g.Count(f => f.AudienciaOferta > 0),
                    Vigentes = g.Count(f => Vigencia.EsVigente(f, hoy)),
                    FechaDesde = g.Min(f => f.FechaDesde),
                    FechaHasta = g.Max(f => f.FechaHasta)
                })
                .OrderBy(c => c.Campana)
                .ToList();

            return Ok(resumen);
        }

        /// <summary>
        /// Cierra una campaña entera: le pone FechaHasta a la fecha que se pida (por defecto ayer,
        /// o sea "que deje de aplicarse ya").
        ///
        /// Es la operación PREFERIBLE al borrado y por eso existe: deja la traza de qué se hizo y
        /// cuándo, permite consultar el histórico, y es reversible quitando la fecha. Borrar solo
        /// tiene sentido para limpiar de verdad.
        /// </summary>
        [HttpPut]
        [Route("api/Campanas/PorNombre/{nombre}/Cerrar")]
        [ResponseType(typeof(ResultadoOperacionCampanaDTO))]
        public async Task<IHttpActionResult> CerrarCampana(string nombre, DateTime? fechaFin = null)
        {
            List<DescuentosProducto> filas = await BuscarPorNombre(nombre).ConfigureAwait(false);
            if (!filas.Any())
            {
                return NotFound();
            }

            DateTime fin = fechaFin ?? DateTime.Today.AddDays(-1);
            string usuario = UsuarioAuditoriaHelper.Resolver(User, null);

            // Se calcula ANTES de tocar nada: después de cerrarlas, las filas ya no viajan y el
            // alcance saldría vacío — la tienda se quedaría con la oferta puesta.
            List<string> aRepublicar = await ProductosQueCambianDeMensaje(filas).ConfigureAwait(false);

            foreach (DescuentosProducto fila in filas)
            {
                fila.FechaHasta = fin;
                fila.Usuario = usuario;
                fila.Fecha_Modificación = DateTime.Now;
            }
            _ = await db.SaveChangesAsync().ConfigureAwait(false);

            foreach (string producto in aRepublicar)
            {
                _ = await db.EncolarProductoSync(producto, usuario).ConfigureAwait(false);
            }

            return Ok(new ResultadoOperacionCampanaDTO
            {
                Campana = nombre?.Trim(),
                FilasAfectadas = filas.Count,
                ProductosEncolados = aRepublicar.Count
            });
        }

        /// <summary>
        /// Borra una campaña entera. Es lo que el 31/08/2026 hubo que hacer con un DELETE por SQL
        /// sobre una ventana de cinco minutos del reloj.
        ///
        /// Lo importante aquí es lo que NO hace: solo encola los productos cuyo mensaje cambia de
        /// verdad. Las 2.017 filas de las rebajas de verano son todas de audiencia 0, así que
        /// borrarlas encola CERO productos — sin ese filtro serían dos mil republicaciones inútiles
        /// contra PrestaShop para no cambiar ningún precio.
        /// </summary>
        [HttpDelete]
        [Route("api/Campanas/PorNombre/{nombre}")]
        [ResponseType(typeof(ResultadoOperacionCampanaDTO))]
        public async Task<IHttpActionResult> DeleteCampanaPorNombre(string nombre)
        {
            List<DescuentosProducto> filas = await BuscarPorNombre(nombre).ConfigureAwait(false);
            if (!filas.Any())
            {
                return NotFound();
            }

            string usuario = UsuarioAuditoriaHelper.Resolver(User, null);
            List<string> aRepublicar = await ProductosQueCambianDeMensaje(filas).ConfigureAwait(false);

            db.DescuentosProductoes.RemoveRange(filas);
            _ = await db.SaveChangesAsync().ConfigureAwait(false);

            foreach (string producto in aRepublicar)
            {
                _ = await db.EncolarProductoSync(producto, usuario).ConfigureAwait(false);
            }

            return Ok(new ResultadoOperacionCampanaDTO
            {
                Campana = nombre?.Trim(),
                FilasAfectadas = filas.Count,
                ProductosEncolados = aRepublicar.Count
            });
        }

        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Las filas de UNA campaña. Solo mira entre las de tarifa: aunque alguien hubiera
        /// etiquetado a mano una fila de cliente o de proveedor, una operación en bloque desde la
        /// pantalla de campañas no puede llevársela por delante.
        /// </summary>
        private async Task<List<DescuentosProducto>> BuscarPorNombre(string nombre)
        {
            string limpio = nombre?.Trim();
            if (string.IsNullOrEmpty(limpio))
            {
                return new List<DescuentosProducto>();
            }

            List<DescuentosProducto> filas = await FilasDeTarifa()
                .Where(d => d.Campana != null)
                .ToListAsync().ConfigureAwait(false);

            return filas.Where(f => string.Equals(f.Campana.Trim(), limpio, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private IQueryable<DescuentosProducto> FilasDeTarifa()
        {
            return db.DescuentosProductoes.Where(d => d.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO
                && (d.Nº_Cliente == null || d.Nº_Cliente.Trim() == string.Empty)
                && (d.NºProveedor == null || d.NºProveedor.Trim() == string.Empty)
                && d.FiltroProducto == null
                && d.CantidadMínima < 2);
        }

        private async Task<DescuentosProducto> BuscarDeTarifa(int id)
        {
            return await FilasDeTarifa().FirstOrDefaultAsync(d => d.Nº_Orden == id).ConfigureAwait(false);
        }

        /// <summary>
        /// Copia suelta de la fila para calcular el alcance ANTERIOR sin que EF nos la cambie por
        /// debajo al asignarle los valores nuevos (la entidad está siendo rastreada).
        /// </summary>
        private static DescuentosProducto Copia(DescuentosProducto fila)
        {
            return new DescuentosProducto
            {
                Empresa = fila.Empresa,
                Nº_Producto = fila.Nº_Producto,
                Familia = fila.Familia,
                GrupoProducto = fila.GrupoProducto,
                // Sin la audiencia no se puede saber si la fila VIAJABA antes: un cambio de 2 a 0
                // tiene que retirar la oferta de la tienda, y eso solo se ve mirando el valor viejo.
                AudienciaOferta = fila.AudienciaOferta
            };
        }

        private async Task Republicar(DescuentosProducto fila, string usuario, List<string> tambien = null)
        {
            List<string> productos = await ProductosQueCambianDeMensaje(new List<DescuentosProducto> { fila })
                .ConfigureAwait(false);

            foreach (string producto in productos.Union(tambien ?? new List<string>()))
            {
                _ = await db.EncolarProductoSync(producto, usuario).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Los productos cuyo MENSAJE cambia por culpa de estas filas — que no es lo mismo que los
        /// productos a los que las filas alcanzan.
        ///
        /// Una fila con AudienciaOferta 0 no viaja a la tienda (lo filtra
        /// <c>CargarDescuentosPorAudiencia</c>), así que crearla, cambiarla o borrarla no cambia ni
        /// un byte del mensaje: republicar por ella son 3 stocks y 2 llamadas HTTP por producto
        /// para mandar exactamente lo mismo.
        ///
        /// Importa de verdad en el borrado en bloque: las 2.017 filas de las rebajas de verano de
        /// 2026 son todas de audiencia 0, así que borrar esa campaña entera debe encolar CERO
        /// productos. Sin este filtro serían más de dos mil republicaciones inútiles contra
        /// PrestaShop, horas de job, para no cambiar ningún precio.
        /// </summary>
        private async Task<List<string>> ProductosQueCambianDeMensaje(List<DescuentosProducto> filas)
        {
            List<DescuentosProducto> queViajan = filas.Where(f => f.AudienciaOferta > 0).ToList();
            return queViajan.Any()
                ? await AlcanceCampanas.ProductosAfectados(db, queViajan).ConfigureAwait(false)
                : new List<string>();
        }

        private static bool Vacio(string texto) => string.IsNullOrWhiteSpace(texto);

        private async Task<string> Validar(CampanaDTO campana, int? idQueSeEdita)
        {
            if (campana == null)
            {
                return "No ha llegado ninguna campaña";
            }

            bool tieneProducto = !Vacio(campana.Producto);
            bool tieneFamilia = !Vacio(campana.Familia);
            bool tieneGrupo = !Vacio(campana.Grupo);

            // El grupo se comprueba ANTES que el resto: "solo grupo" es el error que más se va a
            // cometer (es el nivel que uno espera que exista) y merece su explicación, no el
            // mensaje genérico de "producto o familia". El motor de precios no tiene ningún nivel
            // de tarifa que mire solo el grupo: exige familia Y grupo a la vez, así que una
            // campaña solo por grupo no se la cobraría nadie.
            if (tieneGrupo && !tieneFamilia)
            {
                return "El grupo solo se puede usar junto a una familia: no existe una campaña solo por grupo";
            }
            if (tieneProducto == tieneFamilia)
            {
                return "La campaña tiene que ser de un producto O de una familia, no de las dos cosas ni de ninguna";
            }

            if (campana.Descuento < 0M || campana.Descuento > 1M)
            {
                return "El descuento va en tanto por uno, entre 0 y 1 (0,20 = 20 %)";
            }
            if (campana.DescuentoPublico.HasValue &&
                (campana.DescuentoPublico.Value < 0M || campana.DescuentoPublico.Value > 1M))
            {
                return "El descuento del público va en tanto por uno, entre 0 y 1 (0,20 = 20 %)";
            }

            // Mismo criterio que CK_DescuentosProducto_Audiencia, aquí para dar un mensaje decente
            // en vez de un error de restricción de SQL Server.
            if (campana.AudienciaOferta > 2)
            {
                return "La audiencia solo puede ser 0 (no va a la web), 1 (solo profesionales) o 2 (ambos). " +
                       "El 3 (solo público) está prohibido: el motor de precios no mira la audiencia, " +
                       "así que le descontaría igual al profesional en el pedido";
            }

            // La columna es nvarchar(50): mejor un mensaje que un error de truncamiento de SQL.
            if (!Vacio(campana.Campana) && campana.Campana.Trim().Length > 50)
            {
                return "El nombre de la campaña no puede pasar de 50 caracteres";
            }

            if (campana.FechaDesde.HasValue && campana.FechaHasta.HasValue
                && campana.FechaDesde.Value > campana.FechaHasta.Value)
            {
                return "La fecha de inicio no puede ser posterior a la de fin";
            }

            if (tieneProducto)
            {
                string producto = campana.Producto.Trim();
                bool existe = await db.Productos
                    .AnyAsync(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && p.Número == producto)
                    .ConfigureAwait(false);
                if (!existe)
                {
                    return $"El producto {producto} no existe";
                }
            }
            else
            {
                string familia = campana.Familia.Trim();
                bool existe = await db.Familias
                    .AnyAsync(f => f.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && f.Número == familia)
                    .ConfigureAwait(false);
                if (!existe)
                {
                    return $"La familia {familia} no existe";
                }
            }

            return await ValidarSolape(campana, idQueSeEdita).ConfigureAwait(false);
        }

        /// <summary>
        /// Dos campañas vigentes a la vez en el mismo nivel son un duplicado de los de #229: el
        /// motor las busca con SingleOrDefault y revienta el cálculo del precio del producto.
        /// Vale encadenarlas (una acaba, la otra empieza), que es justo para lo que están las
        /// fechas; lo que no vale es solaparlas.
        /// </summary>
        private async Task<string> ValidarSolape(CampanaDTO campana, int? idQueSeEdita)
        {
            string producto = Vacio(campana.Producto) ? null : campana.Producto.Trim();
            string familia = Vacio(campana.Familia) ? null : campana.Familia.Trim();
            string grupo = Vacio(campana.Grupo) ? null : campana.Grupo.Trim();

            List<DescuentosProducto> mismoNivel = await FilasDeTarifa().ToListAsync().ConfigureAwait(false);

            DescuentosProducto choca = mismoNivel.FirstOrDefault(f =>
                f.Nº_Orden != (idQueSeEdita ?? 0)
                && f.Nº_Producto?.Trim() == producto
                && f.Familia?.Trim() == familia
                && f.GrupoProducto?.Trim() == grupo
                && SeSolapan(f.FechaDesde, f.FechaHasta, campana.FechaDesde, campana.FechaHasta));

            return choca == null
                ? null
                : $"Ya hay otra campaña en el mismo nivel cuyas fechas se solapan (nº {choca.Nº_Orden}). " +
                  "Dos descuentos vigentes a la vez sobre lo mismo rompen el cálculo del precio";
        }

        /// <summary>Dos rangos con extremos abiertos (null = sin límite) se solapan si cada uno
        /// empieza antes de que acabe el otro.</summary>
        internal static bool SeSolapan(DateTime? desdeA, DateTime? hastaA, DateTime? desdeB, DateTime? hastaB)
        {
            bool aEmpiezaAntesDeQueAcabeB = desdeA == null || hastaB == null || desdeA <= hastaB;
            bool bEmpiezaAntesDeQueAcabeA = desdeB == null || hastaA == null || desdeB <= hastaA;
            return aEmpiezaAntesDeQueAcabeB && bEmpiezaAntesDeQueAcabeA;
        }

        private static CampanaDTO ADto(DescuentosProducto fila, DateTime hoy)
        {
            return new CampanaDTO
            {
                Id = fila.Nº_Orden,
                Producto = fila.Nº_Producto?.Trim(),
                Familia = fila.Familia?.Trim(),
                Grupo = fila.GrupoProducto?.Trim(),
                Descuento = fila.Descuento,
                DescuentoPublico = fila.DescuentoPublico,
                AudienciaOferta = fila.AudienciaOferta,
                FechaDesde = fila.FechaDesde,
                FechaHasta = fila.FechaHasta,
                Campana = fila.Campana?.Trim(),
                Vigente = Vigencia.EsVigente(fila, hoy),
                Usuario = fila.Usuario?.Trim(),
                FechaModificacion = fila.Fecha_Modificación
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// NestoAPI#423: una campaña, tal como la ve la pantalla. Es una fila de DescuentosProducto
    /// con los campos que importan y sin los que no (CantidadMínima siempre 1 — las escalonadas
    /// no son campañas —, ni cliente, ni proveedor, ni filtro).
    /// </summary>
    public class CampanaDTO
    {
        /// <summary>Nº Orden de la fila. 0 al crear.</summary>
        public int Id { get; set; }

        public string Producto { get; set; }
        public string Familia { get; set; }
        public string Grupo { get; set; }

        /// <summary>En tanto por uno, como en la tabla: 0,20 = 20 %.</summary>
        public decimal Descuento { get; set; }

        /// <summary>Si va a null, el público hereda el mismo % que el profesional.</summary>
        public decimal? DescuentoPublico { get; set; }

        /// <summary>0 = no va a la web, 1 = solo profesionales, 2 = ambos. El 3 está prohibido.</summary>
        public byte AudienciaOferta { get; set; }

        public DateTime? FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }

        /// <summary>
        /// Nombre de la campaña a la que pertenece la fila ("Rebajas verano 2026", "Black Friday
        /// 2025"...). Es una ETIQUETA, no una entidad: una campaña es "todas las filas que
        /// comparten este texto". Null = no pertenece a ninguna (los descuentos de siempre).
        /// </summary>
        public string Campana { get; set; }

        /// <summary>Calculado, de solo lectura: si la campaña está corriendo hoy.</summary>
        public bool Vigente { get; set; }

        public string Usuario { get; set; }
        public DateTime FechaModificacion { get; set; }
    }

    /// <summary>
    /// NestoAPI#423: una campaña vista por encima, para el filtro de la pantalla y para poder
    /// mirar los números ANTES de operar en bloque.
    /// </summary>
    public class ResumenCampanaDTO
    {
        public string Campana { get; set; }

        /// <summary>Cuántas filas de descuento la componen.</summary>
        public int Filas { get; set; }

        /// <summary>
        /// De esas, cuántas se anuncian de verdad en la tienda (AudienciaOferta mayor que 0).
        /// Puede ser 0 sin que sea un error: las rebajas de verano de 2026 son 2.017 filas y
        /// ninguna viaja, porque se metieron antes de que existiera la audiencia.
        /// </summary>
        public int FilasQueViajan { get; set; }

        /// <summary>Cuántas están corriendo hoy según sus fechas.</summary>
        public int Vigentes { get; set; }

        public DateTime? FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }
    }

    /// <summary>
    /// NestoAPI#423: lo que devuelve una operación en bloque. Los dos números importan y son
    /// distintos: las filas tocadas y los productos que hay que republicar en la tienda.
    /// </summary>
    public class ResultadoOperacionCampanaDTO
    {
        public string Campana { get; set; }
        public int FilasAfectadas { get; set; }
        public int ProductosEncolados { get; set; }
    }
}
