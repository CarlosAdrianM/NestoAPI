using NestoAPI.Infraestructure.AlmacenesFicticios;
using NestoAPI.Infraestructure.Eventos;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.ExtractosProducto;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Infraestructure.Seguridad;
using NestoAPI.Infraestructure.Ubicaciones;
using NestoAPI.Infraestructure.Verifactu;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.ProductosIncompletos
{
    public interface IServicioProductosIncompletos
    {
        /// <summary>Saca una pieza de una unidad: +N de la pieza y la unidad pasa al almacén de incompletos. Con ensayo, comprueba y no escribe.</summary>
        Task<ResultadoProductoIncompletoDTO> SacarPieza(SacarPiezaDTO peticion, bool ensayo, IPrincipal usuario);
        /// <summary>Llega el recambio: −N de la pieza y, si ya no le falta nada, la unidad vuelve a su almacén.</summary>
        Task<ResultadoProductoIncompletoDTO> Completar(int id, CompletarProductoIncompletoDTO peticion, bool ensayo, IPrincipal usuario);
        /// <summary>No se va a completar: la unidad sale del almacén de incompletos (merma con causa conocida).</summary>
        Task<ResultadoProductoIncompletoDTO> DarDeBaja(int id, BajaProductoIncompletoDTO peticion, bool ensayo, IPrincipal usuario);
        Task<ListadoProductosIncompletosDTO> Listar(bool soloVivos, string almacen, string producto, IPrincipal usuario);
        Task<ProductoIncompletoDTO> Leer(int id, IPrincipal usuario);
        Task<List<PiezaSacadaDTO>> PiezasSacadas(string producto, IPrincipal usuario);
        Task<EtiquetaIncompletoDTO> ImprimirEtiqueta(int id, IPrincipal usuario);
    }

    /// <summary>NestoAPI#902: la campana de Nesto a Compras («se debe…»). Nunca lanza.</summary>
    public interface IAvisadorProductoIncompleto
    {
        Task<bool> AvisarACompras(NotificacionPushDTO notificacion);
    }

    public class AvisadorProductoIncompleto : IAvisadorProductoIncompleto
    {
        private const string DOMINIO = "NUEVAVISION\\";
        private readonly Func<IServicioNotificacionesPush> notificaciones;
        private readonly Func<string, List<string>> miembrosDelGrupo;

        public AvisadorProductoIncompleto() : this(null, null)
        {
        }

        internal AvisadorProductoIncompleto(Func<IServicioNotificacionesPush> notificaciones, Func<string, List<string>> miembrosDelGrupo)
        {
            this.notificaciones = notificaciones ?? (() => new ServicioNotificacionesPush());
            this.miembrosDelGrupo = miembrosDelGrupo ?? MiembrosGrupoDominio.Leer;
        }

        public async Task<bool> AvisarACompras(NotificacionPushDTO notificacion)
        {
            if (notificacion == null)
            {
                return false;
            }
            try
            {
                List<string> usuarios = (miembrosDelGrupo(Constantes.GruposSeguridad.COMPRAS) ?? new List<string>())
                    .Select(u => u?.Trim())
                    .Where(u => !string.IsNullOrWhiteSpace(u) && !u.StartsWith("(", StringComparison.Ordinal))
                    .Select(u => u.Contains("\\") ? u : DOMINIO + u)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (!usuarios.Any())
                {
                    ElmahHelper.Log(new Exception($"[Productos incompletos #902] No hay nadie en Compras a quien avisar: {notificacion.Cuerpo}"));
                    return false;
                }
                IServicioNotificacionesPush servicio = notificaciones();
                foreach (string usuario in usuarios)
                {
                    await servicio.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
                }
                return true;
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Productos incompletos #902] No se ha podido avisar a Compras: {ex.Message}", ex));
                return false;
            }
        }
    }

    /// <summary>NestoAPI#902: la etiqueta «INCOMPLETO» por la impresora de etiquetas de producto del usuario. Nunca lanza.</summary>
    public interface IImpresoraEtiquetaIncompleto
    {
        EtiquetaIncompletoDTO Imprimir(ProductoIncompletoDTO ficha, string usuario, bool ensayo);
    }

    public class ImpresoraEtiquetaIncompleto : IImpresoraEtiquetaIncompleto
    {
        private readonly Func<ILectorParametrosUsuario> parametros;
        private readonly Func<IImpresoraEtiquetasAlmacen> impresora;

        public ImpresoraEtiquetaIncompleto() : this(null, null)
        {
        }

        internal ImpresoraEtiquetaIncompleto(Func<ILectorParametrosUsuario> parametros, Func<IImpresoraEtiquetasAlmacen> impresora)
        {
            this.parametros = parametros ?? (() => new LectorParametrosUsuario());
            this.impresora = impresora ?? (() => ImpresoraEtiquetasConAjuste.EnWindows());
        }

        public EtiquetaIncompletoDTO Imprimir(ProductoIncompletoDTO ficha, string usuario, bool ensayo)
        {
            var etiqueta = new EtiquetaIncompletoDTO
            {
                Lineas = new[] { GeneradorEtiquetaIncompleto.CABECERA }.Concat(GeneradorEtiquetaIncompleto.Lineas(ficha)).ToList(),
                Codigo = ficha?.CodigoEtiqueta
            };
            try
            {
                // La MISMA impresora que las etiquetas de producto y de hueco (ImpresoraCodBarras del usuario o la «(defecto)»)
                etiqueta.Impresora = ServicioEtiquetasHueco.ImpresoraDelUsuario(parametros(), Constantes.Empresas.EMPRESA_POR_DEFECTO, usuario);
                if (ensayo || ficha == null || ficha.Id <= 0)
                {
                    etiqueta.Mensaje = $"Se imprimiría la etiqueta INCOMPLETO en {etiqueta.Impresora}.";
                    return etiqueta;
                }
                _ = impresora().Imprimir(etiqueta.Impresora, GeneradorEtiquetaIncompleto.Trabajo(ficha));
                etiqueta.Impresa = true;
                etiqueta.Mensaje = $"Etiqueta INCOMPLETO mandada a {etiqueta.Impresora}.";
            }
            catch (Exception ex)
            {
                if (!(ex is ArgumentException))
                {
                    ElmahHelper.Log(new Exception($"[Productos incompletos #902] No se ha podido imprimir la etiqueta de la ficha {ficha?.Id}: {ex.Message}", ex));
                }
                etiqueta.Mensaje = $"No se ha podido imprimir la etiqueta INCOMPLETO: {ex.Message} Reimprímala desde la ficha.";
            }
            return etiqueta;
        }
    }

    /// <summary>
    /// NestoAPI#902: sacar una pieza de un producto, completarlo y darlo de baja. Las reglas, en <see cref="ReglasProductosIncompletos"/>;
    /// la escritura, en UNA transacción: applock, las líneas en ExtractoProducto por el núcleo <see cref="MovimientoTraspasoStockSql"/>
    /// (PreExtrProducto + prdExtrProducto por su único punto de llamada, diarios «_SacaPieza» y «_BajaIncom», un NºTraspaso de
    /// ContadoresGlobales), los huecos de Algete por la puerta única de Ubicaciones, la ficha en dbo.ProductosIncompletos y el rastro en
    /// Modificaciones. Después, sin romper nunca: la etiqueta «INCOMPLETO» y la campana a Compras.
    /// </summary>
    public class ServicioProductosIncompletos : IServicioProductosIncompletos
    {
        private readonly IRepositorioProductosIncompletos repositorio;
        private readonly IAvisadorProductoIncompleto avisador;
        private readonly IImpresoraEtiquetaIncompleto impresora;

        public ServicioProductosIncompletos(NVEntities db)
            : this(new RepositorioProductosIncompletosSql(db), new AvisadorProductoIncompleto(), new ImpresoraEtiquetaIncompleto())
        {
        }

        internal ServicioProductosIncompletos(IRepositorioProductosIncompletos repositorio, IAvisadorProductoIncompleto avisador, IImpresoraEtiquetaIncompleto impresora)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.avisador = avisador;
            this.impresora = impresora;
        }

        private static string Empresa => Constantes.Empresas.EMPRESA_POR_DEFECTO;

        private static NestoBusinessException Error(string mensaje, HttpStatusCode codigo = HttpStatusCode.BadRequest) =>
            new NestoBusinessException(mensaje) { StatusCode = codigo };

        internal async Task<ContextoUsuarioIncompletos> Contexto(IPrincipal usuario)
        {
            if (usuario?.Identity?.IsAuthenticated != true)
            {
                throw Error("Hace falta iniciar sesión.", HttpStatusCode.Unauthorized);
            }
            if (ValidadorAccesoCliente.EsClienteTienda(usuario))
            {
                throw Error(ReglasProductosIncompletos.MENSAJE_SIN_PERMISO_CONSULTAR, HttpStatusCode.Forbidden);
            }
            var contexto = new ContextoUsuarioIncompletos
            {
                Usuario = PermisosEventos.SinDominio(usuario.Identity.Name),
                EsDireccion = usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION),
                EsInformatica = usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.INFORMATICA),
                EsAlmacen = usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.ALMACEN),
                EsTiendas = usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.TIENDAS),
                EsCompras = usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.COMPRAS)
            };
            if (contexto.EsTiendas)
            {
                contexto.AlmacenTienda = GestorFacturacionTienda.NormalizarTienda(
                    await repositorio.LeerParametroPropio(contexto.Usuario, GestorFacturacionTienda.CLAVE_ALMACEN_USUARIO).ConfigureAwait(false));
            }
            return contexto;
        }

        private async Task ComprobarTabla()
        {
            if (!await repositorio.TablaCreada().ConfigureAwait(false))
            {
                throw Error(ReglasProductosIncompletos.MENSAJE_FALTA_SCRIPT, HttpStatusCode.ServiceUnavailable);
            }
        }

        private async Task<ContextoUsuarioIncompletos> ParaOperar(IPrincipal usuario)
        {
            ContextoUsuarioIncompletos contexto = await Contexto(usuario).ConfigureAwait(false);
            if (!ReglasProductosIncompletos.PuedeOperarAlguno(contexto))
            {
                throw Error(ReglasProductosIncompletos.MENSAJE_SIN_PERMISO, HttpStatusCode.Forbidden);
            }
            return contexto;
        }

        private async Task<ContextoUsuarioIncompletos> ParaConsultar(IPrincipal usuario)
        {
            ContextoUsuarioIncompletos contexto = await Contexto(usuario).ConfigureAwait(false);
            if (!ReglasProductosIncompletos.PuedeConsultar(contexto))
            {
                throw Error(ReglasProductosIncompletos.MENSAJE_SIN_PERMISO_CONSULTAR, HttpStatusCode.Forbidden);
            }
            return contexto;
        }

        private static void ComprobarPermiso(ContextoUsuarioIncompletos contexto, string almacen)
        {
            string motivo = ReglasProductosIncompletos.MotivoNoPuedeOperar(contexto, almacen);
            if (motivo != null)
            {
                throw Error(motivo, HttpStatusCode.Forbidden);
            }
        }

        /// <summary>El almacén de la unidad (activo) y el de incompletos (el del parámetro, o el de la ficha si ya está creada).</summary>
        private async Task<(AlmacenLeido Origen, AlmacenLeido Incompletos)> Almacenes(string almacen, string incompletosDeLaFicha)
        {
            List<AlmacenLeido> almacenes = await repositorio.LeerAlmacenes().ConfigureAwait(false) ?? new List<AlmacenLeido>();
            string codigoIncompletos = incompletosDeLaFicha ?? await repositorio.LeerAlmacenIncompletos().ConfigureAwait(false);
            string motivo = ReglasProductosIncompletos.MotivoAlmacenIncompletosNoValido(codigoIncompletos, almacenes);
            if (motivo != null)
            {
                throw Error(motivo, HttpStatusCode.ServiceUnavailable);
            }
            AlmacenLeido incompletos = almacenes.First(a => string.Equals(a.Codigo?.Trim(), codigoIncompletos.Trim(), StringComparison.OrdinalIgnoreCase));
            AlmacenLeido origen = almacenes.FirstOrDefault(a => string.Equals(a.Codigo?.Trim(), almacen?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (origen == null)
            {
                throw Error($"El almacén {almacen?.Trim()} no existe o no está activo.");
            }
            return (origen, incompletos);
        }

        // ---------------------------------------------------------------- Sacar una pieza

        public async Task<ResultadoProductoIncompletoDTO> SacarPieza(SacarPiezaDTO peticion, bool ensayo, IPrincipal usuario)
        {
            ContextoUsuarioIncompletos contexto = await ParaOperar(usuario).ConfigureAwait(false);
            string invalida = ReglasProductosIncompletos.MotivoPeticionNoValida(peticion);
            if (invalida != null)
            {
                throw Error(invalida);
            }
            await ComprobarTabla().ConfigureAwait(false);
            string almacen = ReglasProductosIncompletos.Codigo(peticion.Almacen);
            ComprobarPermiso(contexto, almacen);
            (AlmacenLeido origen, AlmacenLeido incompletos) = await Almacenes(almacen, null).ConfigureAwait(false);
            string codigoIncompletos = incompletos.Codigo.Trim().ToUpperInvariant();

            string donante = ReglasProductosIncompletos.Codigo(peticion.ProductoDonante);
            string pieza = ReglasProductosIncompletos.Codigo(peticion.Pieza);
            string serie = ReglasProductosIncompletos.Limpio(peticion.NumeroSerie);
            Dictionary<string, ProductoIncompletoLeido> productos = await repositorio.LeerProductos(new[] { donante, pieza }).ConfigureAwait(false)
                ?? new Dictionary<string, ProductoIncompletoLeido>(StringComparer.OrdinalIgnoreCase);
            productos.TryGetValue(donante, out ProductoIncompletoLeido fichaDonante);
            productos.TryGetValue(pieza, out ProductoIncompletoLeido fichaPieza);
            List<string> motivos = ReglasProductosIncompletos.MotivosProductos(peticion, fichaDonante, fichaPieza);
            if (motivos.Any())
            {
                throw Error(string.Join(" ", motivos));
            }

            List<ProductoIncompletoDTO> vivas = await repositorio.LeerFichas(true, null, donante).ConfigureAwait(false) ?? new List<ProductoIncompletoDTO>();
            ProductoIncompletoDTO apartada = ReglasProductosIncompletos.UnidadYaApartada(vivas, donante, serie);
            string enOtro = ReglasProductosIncompletos.MotivoUnidadEnOtroAlmacen(apartada, almacen);
            if (enOtro != null)
            {
                throw Error(enOtro);
            }
            bool mover = apartada == null;
            if (mover)
            {
                string sinStock = ReglasProductosIncompletos.MotivoSinStock(donante, almacen, 1,
                    await repositorio.LeerStock(almacen, new[] { donante }).ConfigureAwait(false), origen.PermiteNegativo, "la unidad de la que se saca");
                if (sinStock != null)
                {
                    throw Error(sinStock);
                }
            }

            // Algete: de qué hueco sale la unidad (propuesto si no viene); la pieza entra pendiente de ubicar
            bool donanteConHuecos = mover && origen.ControlUbicaciones && fichaDonante.Ubicar;
            HuecoLibreFicticio hueco = null;
            bool huecoPropuesto = false;
            if (donanteConHuecos)
            {
                List<HuecoLibreFicticio> huecos = await repositorio.LeerHuecosLibres(almacen, new[] { donante }).ConfigureAwait(false);
                List<LineaTraspasoFicticioPlan> plan = ReglasAlmacenesFicticios.AsignarHuecos(
                    new[] { new LineaTraspasoFicticioDTO { Producto = donante, Cantidad = 1, Pasillo = peticion.Pasillo, Fila = peticion.Fila, Columna = peticion.Columna } },
                    huecos, _ => true, almacen, out motivos);
                if (motivos.Any())
                {
                    throw Error(string.Join(" ", motivos));
                }
                hueco = plan[0].Hueco;
                huecoPropuesto = plan[0].HuecoPropuesto;
            }
            int piezaPendiente = origen.ControlUbicaciones && fichaPieza.Ubicar ? peticion.Cantidad : 0;

            string quien = UsuarioAuditoriaHelper.ParaAuditoria(usuario.Identity.Name);
            var ficha = new ProductoIncompletoDTO
            {
                Empresa = Empresa,
                Almacen = almacen,
                AlmacenIncompletos = mover ? codigoIncompletos : apartada.AlmacenIncompletos?.Trim(),
                ProductoDonante = fichaDonante.Producto.Trim(),
                NombreDonante = fichaDonante.Nombre?.Trim(),
                NumeroSerie = serie,
                Pieza = fichaPieza.Producto.Trim(),
                NombrePieza = fichaPieza.Nombre?.Trim(),
                Cantidad = peticion.Cantidad,
                Pedido = peticion.Pedido,
                Comentario = ReglasProductosIncompletos.Limpio(peticion.Comentario),
                HuecoOrigen = mover ? hueco?.Texto : apartada.HuecoOrigen,
                HuecoDestino = HuecoUbicacion.DeCodigo(peticion.HuecoDestino)?.ToString() ?? (mover ? null : apartada.HuecoDestino),
                Estado = EstadosProductoIncompleto.INCOMPLETO,
                Usuario = quien,
                Fecha = DateTime.Now,
                PvpDonante = fichaDonante.Pvp
            };
            List<LineaMovimientoTraspaso> lineas = ReglasProductosIncompletos.LineasSacar(almacen, ficha.AlmacenIncompletos, ficha.ProductoDonante, serie,
                ficha.Pieza, ficha.Cantidad, mover);

            if (ensayo)
            {
                ResultadoProductoIncompletoDTO prueba = ResultadoSacar(ficha, true, null, mover, lineas, hueco, huecoPropuesto, piezaPendiente);
                prueba.Etiqueta = impresora?.Imprimir(ficha, usuario.Identity.Name, true);
                return prueba;
            }

            bool repetida = false;
            ResultadoProductoIncompletoDTO hecho = await repositorio.EnTransaccion(async t =>
            {
                await t.Bloquear(Empresa).ConfigureAwait(false);
                if (peticion.Clave.HasValue)
                {
                    ProductoIncompletoDTO anterior = await t.LeerPorClave(peticion.Clave.Value).ConfigureAwait(false);
                    if (anterior != null)
                    {
                        repetida = true;
                        ResultadoProductoIncompletoDTO otra = ResultadoSacar(ReglasProductosIncompletos.Completar(anterior, DateTime.Today), false, anterior.Traspaso,
                            false, new List<LineaMovimientoTraspaso>(), null, false, 0);
                        otra.Mensaje = $"Esta pieza ya estaba sacada (ficha {anterior.Id}): no se ha repetido nada.";
                        return otra;
                    }
                }
                // Lo leído puede haber cambiado desde la comprobación: se vuelve a mirar con el bloqueo puesto
                List<ProductoIncompletoDTO> vivasAhora = await t.LeerVivasDelDonante(donante).ConfigureAwait(false);
                if ((ReglasProductosIncompletos.UnidadYaApartada(vivasAhora, donante, serie) == null) != mover)
                {
                    throw Error("La unidad ha cambiado mientras tanto (otra persona le ha sacado o puesto una pieza): vuelva a intentarlo. No se ha hecho nada.",
                        HttpStatusCode.Conflict);
                }
                if (mover)
                {
                    string sinStock = ReglasProductosIncompletos.MotivoSinStock(donante, almacen, 1,
                        await t.LeerStock(almacen, new[] { donante }).ConfigureAwait(false), origen.PermiteNegativo, "la unidad de la que se saca");
                    if (sinStock != null)
                    {
                        throw Error(sinStock + " El stock ha cambiado mientras tanto: no se ha hecho nada.", HttpStatusCode.Conflict);
                    }
                }
                int traspaso = await t.SiguienteTraspaso().ConfigureAwait(false);
                await t.Contabilizar(ReglasProductosIncompletos.DIARIO, traspaso, lineas, quien).ConfigureAwait(false);
                if (donanteConHuecos)
                {
                    await t.SacarDeUbicaciones(almacen, donante, HuecoUbicacion.De(hueco.Pasillo, hueco.Fila, hueco.Columna), 1, traspaso, quien).ConfigureAwait(false);
                }
                if (piezaPendiente > 0)
                {
                    await t.DejarPendienteDeUbicar(almacen, ficha.Pieza, piezaPendiente, traspaso, quien).ConfigureAwait(false);
                }
                ficha.Traspaso = traspaso;
                ficha.Id = await t.Insertar(ficha, peticion.Clave).ConfigureAwait(false);
                ResultadoProductoIncompletoDTO resultado = ResultadoSacar(ReglasProductosIncompletos.Completar(ficha, DateTime.Today), false, traspaso, mover, lineas,
                    hueco, huecoPropuesto, piezaPendiente);
                await t.ApuntarRastro($"Sacar pieza · ficha {ficha.Id}", Rastro(resultado), quien).ConfigureAwait(false);
                return resultado;
            }).ConfigureAwait(false);

            if (!repetida)
            {
                hecho.Etiqueta = impresora?.Imprimir(hecho.Ficha, usuario.Identity.Name, false);
                if (avisador != null)
                {
                    hecho.AvisoCompras = await avisador.AvisarACompras(ReglasProductosIncompletos.NotificacionCompras(hecho.Ficha)).ConfigureAwait(false);
                }
            }
            return hecho;
        }

        private static ResultadoProductoIncompletoDTO ResultadoSacar(ProductoIncompletoDTO ficha, bool ensayo, int? traspaso, bool mover,
            List<LineaMovimientoTraspaso> lineas, HuecoLibreFicticio hueco, bool huecoPropuesto, int piezaPendiente)
        {
            ReglasProductosIncompletos.Completar(ficha, DateTime.Today);
            return new ResultadoProductoIncompletoDTO
            {
                Ensayo = ensayo,
                Operacion = "SacarPieza",
                Ficha = ficha,
                NumeroTraspaso = traspaso,
                UnidadMovida = mover,
                Apuntes = ReglasProductosIncompletos.Apuntes(ReglasProductosIncompletos.DIARIO, lineas),
                Hueco = hueco?.Texto,
                HuecoPropuesto = hueco != null && huecoPropuesto,
                UnidadesPendientesDeUbicar = piezaPendiente,
                Mensaje = ReglasProductosIncompletos.MensajeSacar(ficha, ensayo, mover, piezaPendiente, hueco?.Texto)
            };
        }

        // ---------------------------------------------------------------- Completar

        public async Task<ResultadoProductoIncompletoDTO> Completar(int id, CompletarProductoIncompletoDTO peticion, bool ensayo, IPrincipal usuario)
        {
            ContextoUsuarioIncompletos contexto = await ParaOperar(usuario).ConfigureAwait(false);
            peticion = peticion ?? new CompletarProductoIncompletoDTO();
            string invalida = ReglasProductosIncompletos.MotivoCompletarNoValido(peticion);
            if (invalida != null)
            {
                throw Error(invalida);
            }
            await ComprobarTabla().ConfigureAwait(false);
            ProductoIncompletoDTO ficha = await FichaParaCerrar(id, contexto).ConfigureAwait(false);
            string almacen = ficha.Almacen.Trim().ToUpperInvariant();
            (AlmacenLeido origen, AlmacenLeido incompletos) = await Almacenes(almacen, ficha.AlmacenIncompletos).ConfigureAwait(false);

            Dictionary<string, ProductoIncompletoLeido> productos = await repositorio.LeerProductos(new[] { ficha.ProductoDonante, ficha.Pieza }).ConfigureAwait(false)
                ?? new Dictionary<string, ProductoIncompletoLeido>(StringComparer.OrdinalIgnoreCase);
            productos.TryGetValue(ficha.ProductoDonante.Trim(), out ProductoIncompletoLeido fichaDonante);
            productos.TryGetValue(ficha.Pieza.Trim(), out ProductoIncompletoLeido fichaPieza);

            List<ProductoIncompletoDTO> vivas = await repositorio.LeerFichas(true, null, ficha.ProductoDonante).ConfigureAwait(false) ?? new List<ProductoIncompletoDTO>();
            int otras = ReglasProductosIncompletos.OtrasDeLaUnidad(ficha, vivas).Count;
            bool vuelve = otras == 0;

            string sinPieza = ReglasProductosIncompletos.MotivoSinStock(ficha.Pieza, almacen, ficha.Cantidad,
                await repositorio.LeerStock(almacen, new[] { ficha.Pieza }).ConfigureAwait(false), origen.PermiteNegativo, "la pieza que lo completa; ¿ha llegado ya el recambio?");
            if (sinPieza != null)
            {
                throw Error(sinPieza);
            }
            if (vuelve)
            {
                string sinUnidad = ReglasProductosIncompletos.MotivoSinStock(ficha.ProductoDonante, incompletos.Codigo, 1,
                    await repositorio.LeerStock(incompletos.Codigo.Trim(), new[] { ficha.ProductoDonante }).ConfigureAwait(false), false, "la unidad incompleta");
                if (sinUnidad != null)
                {
                    throw Error(sinUnidad);
                }
            }

            bool piezaConHuecos = origen.ControlUbicaciones && fichaPieza?.Ubicar == true;
            HuecoUbicacion huecoPieza = piezaConHuecos ? ReglasProductosIncompletos.Hueco(peticion.Pasillo, peticion.Fila, peticion.Columna) : null;
            if (huecoPieza != null)
            {
                List<HuecoLibreFicticio> huecos = await repositorio.LeerHuecosLibres(almacen, new[] { ficha.Pieza }).ConfigureAwait(false);
                ReglasAlmacenesFicticios.AsignarHuecos(
                    new[] { new LineaTraspasoFicticioDTO { Producto = ficha.Pieza.Trim(), Cantidad = ficha.Cantidad, Pasillo = huecoPieza.Pasillo, Fila = huecoPieza.Fila, Columna = huecoPieza.Columna } },
                    huecos, _ => true, almacen, out List<string> motivos);
                if (motivos.Any())
                {
                    throw Error(string.Join(" ", motivos));
                }
            }
            int unidadPendiente = vuelve && origen.ControlUbicaciones && fichaDonante?.Ubicar == true ? 1 : 0;
            List<LineaMovimientoTraspaso> lineas = ReglasProductosIncompletos.LineasCompletar(ficha, vuelve);
            string quien = UsuarioAuditoriaHelper.ParaAuditoria(usuario.Identity.Name);
            string comentario = ReglasProductosIncompletos.Limpio(peticion.Comentario);

            if (ensayo)
            {
                return ResultadoCompletar(ficha, true, null, vuelve, otras, lineas, huecoPieza, piezaConHuecos, unidadPendiente);
            }

            return await repositorio.EnTransaccion(async t =>
            {
                await t.Bloquear(Empresa).ConfigureAwait(false);
                ProductoIncompletoDTO ahora = await t.LeerFicha(id).ConfigureAwait(false);
                string cerrada = ReglasProductosIncompletos.MotivoNoSePuedeCerrar(ahora);
                if (ahora == null || cerrada != null)
                {
                    throw Error(cerrada ?? $"La ficha {id} ya no existe.", HttpStatusCode.Conflict);
                }
                List<ProductoIncompletoDTO> vivasAhora = await t.LeerVivasDelDonante(ficha.ProductoDonante).ConfigureAwait(false);
                if ((ReglasProductosIncompletos.OtrasDeLaUnidad(ficha, vivasAhora).Count == 0) != vuelve)
                {
                    throw Error("La unidad ha cambiado mientras tanto (otra persona le ha sacado o puesto una pieza): vuelva a intentarlo. No se ha hecho nada.",
                        HttpStatusCode.Conflict);
                }
                string sinPiezaAhora = ReglasProductosIncompletos.MotivoSinStock(ficha.Pieza, almacen, ficha.Cantidad,
                    await t.LeerStock(almacen, new[] { ficha.Pieza }).ConfigureAwait(false), origen.PermiteNegativo, "la pieza que lo completa");
                if (sinPiezaAhora != null)
                {
                    throw Error(sinPiezaAhora + " El stock ha cambiado mientras tanto: no se ha hecho nada.", HttpStatusCode.Conflict);
                }
                int traspaso = await t.SiguienteTraspaso().ConfigureAwait(false);
                await t.Contabilizar(ReglasProductosIncompletos.DIARIO, traspaso, lineas, quien).ConfigureAwait(false);
                if (piezaConHuecos)
                {
                    await t.SacarDeUbicaciones(almacen, ficha.Pieza.Trim(), huecoPieza, ficha.Cantidad, traspaso, quien).ConfigureAwait(false);
                }
                if (unidadPendiente > 0)
                {
                    await t.DejarPendienteDeUbicar(almacen, ficha.ProductoDonante.Trim(), 1, traspaso, quien).ConfigureAwait(false);
                }
                await t.Cerrar(id, EstadosProductoIncompleto.COMPLETADO, comentario, traspaso, quien).ConfigureAwait(false);
                Cerrada(ficha, EstadosProductoIncompleto.COMPLETADO, comentario, traspaso, quien);
                ResultadoProductoIncompletoDTO resultado = ResultadoCompletar(ficha, false, traspaso, vuelve, otras, lineas, huecoPieza, piezaConHuecos, unidadPendiente);
                await t.ApuntarRastro($"Completar · ficha {id}", Rastro(resultado), quien).ConfigureAwait(false);
                return resultado;
            }).ConfigureAwait(false);
        }

        private static ResultadoProductoIncompletoDTO ResultadoCompletar(ProductoIncompletoDTO ficha, bool ensayo, int? traspaso, bool vuelve, int otras,
            List<LineaMovimientoTraspaso> lineas, HuecoUbicacion huecoPieza, bool piezaConHuecos, int unidadPendiente)
        {
            ReglasProductosIncompletos.Completar(ficha, DateTime.Today);
            string mensaje = ReglasProductosIncompletos.MensajeCompletar(ficha, ensayo, vuelve, otras);
            if (piezaConHuecos)
            {
                mensaje += huecoPieza != null
                    ? $" La pieza sale del hueco {huecoPieza}."
                    : " La pieza sale de lo pendiente de ubicar (o, si no, del único hueco donde está).";
            }
            return new ResultadoProductoIncompletoDTO
            {
                Ensayo = ensayo,
                Operacion = "Completar",
                Ficha = ficha,
                NumeroTraspaso = traspaso,
                UnidadMovida = vuelve,
                Apuntes = ReglasProductosIncompletos.Apuntes(ReglasProductosIncompletos.DIARIO, lineas),
                Hueco = huecoPieza?.ToString(),
                UnidadesPendientesDeUbicar = unidadPendiente,
                Mensaje = mensaje
            };
        }

        // ---------------------------------------------------------------- Baja

        public async Task<ResultadoProductoIncompletoDTO> DarDeBaja(int id, BajaProductoIncompletoDTO peticion, bool ensayo, IPrincipal usuario)
        {
            ContextoUsuarioIncompletos contexto = await ParaOperar(usuario).ConfigureAwait(false);
            string invalida = ReglasProductosIncompletos.MotivoBajaNoValida(peticion);
            if (invalida != null)
            {
                throw Error(invalida);
            }
            await ComprobarTabla().ConfigureAwait(false);
            ProductoIncompletoDTO ficha = await FichaParaCerrar(id, contexto).ConfigureAwait(false);
            AlmacenLeido incompletos = (await Almacenes(ficha.Almacen, ficha.AlmacenIncompletos).ConfigureAwait(false)).Incompletos;
            string motivo = ReglasProductosIncompletos.Limpio(peticion.Motivo);

            List<ProductoIncompletoDTO> vivas = await repositorio.LeerFichas(true, null, ficha.ProductoDonante).ConfigureAwait(false) ?? new List<ProductoIncompletoDTO>();
            List<int> deLaUnidad = new[] { ficha.Id }.Concat(ReglasProductosIncompletos.OtrasDeLaUnidad(ficha, vivas).Select(f => f.Id)).ToList();
            string sinUnidad = ReglasProductosIncompletos.MotivoSinStock(ficha.ProductoDonante, incompletos.Codigo, 1,
                await repositorio.LeerStock(incompletos.Codigo.Trim(), new[] { ficha.ProductoDonante }).ConfigureAwait(false), false, "la unidad incompleta");
            if (sinUnidad != null)
            {
                throw Error(sinUnidad);
            }
            List<LineaMovimientoTraspaso> lineas = ReglasProductosIncompletos.LineasBaja(ficha, motivo);
            string quien = UsuarioAuditoriaHelper.ParaAuditoria(usuario.Identity.Name);

            if (ensayo)
            {
                return ResultadoBaja(ficha, true, null, deLaUnidad, lineas);
            }

            return await repositorio.EnTransaccion(async t =>
            {
                await t.Bloquear(Empresa).ConfigureAwait(false);
                ProductoIncompletoDTO ahora = await t.LeerFicha(id).ConfigureAwait(false);
                string cerrada = ReglasProductosIncompletos.MotivoNoSePuedeCerrar(ahora);
                if (ahora == null || cerrada != null)
                {
                    throw Error(cerrada ?? $"La ficha {id} ya no existe.", HttpStatusCode.Conflict);
                }
                List<ProductoIncompletoDTO> vivasAhora = await t.LeerVivasDelDonante(ficha.ProductoDonante).ConfigureAwait(false);
                List<int> ahoraDeLaUnidad = new[] { ficha.Id }.Concat(ReglasProductosIncompletos.OtrasDeLaUnidad(ficha, vivasAhora).Select(f => f.Id)).ToList();
                string sinUnidadAhora = ReglasProductosIncompletos.MotivoSinStock(ficha.ProductoDonante, incompletos.Codigo, 1,
                    await t.LeerStock(incompletos.Codigo.Trim(), new[] { ficha.ProductoDonante }).ConfigureAwait(false), false, "la unidad incompleta");
                if (sinUnidadAhora != null)
                {
                    throw Error(sinUnidadAhora + " El stock ha cambiado mientras tanto: no se ha hecho nada.", HttpStatusCode.Conflict);
                }
                int traspaso = await t.SiguienteTraspaso().ConfigureAwait(false);
                await t.Contabilizar(ReglasProductosIncompletos.DIARIO_BAJA, traspaso, lineas, quien).ConfigureAwait(false);
                foreach (int cerrar in ahoraDeLaUnidad)
                {
                    await t.Cerrar(cerrar, EstadosProductoIncompleto.BAJA, motivo, traspaso, quien).ConfigureAwait(false);
                }
                Cerrada(ficha, EstadosProductoIncompleto.BAJA, motivo, traspaso, quien);
                ResultadoProductoIncompletoDTO resultado = ResultadoBaja(ficha, false, traspaso, ahoraDeLaUnidad, lineas);
                await t.ApuntarRastro($"Baja · ficha {id}", Rastro(resultado), quien).ConfigureAwait(false);
                return resultado;
            }).ConfigureAwait(false);
        }

        private static ResultadoProductoIncompletoDTO ResultadoBaja(ProductoIncompletoDTO ficha, bool ensayo, int? traspaso, List<int> fichas, List<LineaMovimientoTraspaso> lineas)
        {
            ReglasProductosIncompletos.Completar(ficha, DateTime.Today);
            return new ResultadoProductoIncompletoDTO
            {
                Ensayo = ensayo,
                Operacion = "Baja",
                Ficha = ficha,
                FichasCerradas = fichas,
                NumeroTraspaso = traspaso,
                UnidadMovida = true,
                Apuntes = ReglasProductosIncompletos.Apuntes(ReglasProductosIncompletos.DIARIO_BAJA, lineas),
                Mensaje = ReglasProductosIncompletos.MensajeBaja(ficha, ensayo, fichas.Count)
            };
        }

        /// <summary>La ficha (404), que siga incompleta (409) y que el usuario pueda con su almacén (403).</summary>
        private async Task<ProductoIncompletoDTO> FichaParaCerrar(int id, ContextoUsuarioIncompletos contexto)
        {
            ProductoIncompletoDTO ficha = await repositorio.LeerFicha(id).ConfigureAwait(false)
                ?? throw Error($"No existe la ficha de producto incompleto {id}.", HttpStatusCode.NotFound);
            string cerrada = ReglasProductosIncompletos.MotivoNoSePuedeCerrar(ficha);
            if (cerrada != null)
            {
                throw Error(cerrada, HttpStatusCode.Conflict);
            }
            ComprobarPermiso(contexto, ficha.Almacen);
            return ficha;
        }

        private static void Cerrada(ProductoIncompletoDTO ficha, string estado, string motivo, int traspaso, string usuario)
        {
            ficha.Estado = estado;
            ficha.FechaCierre = DateTime.Now;
            ficha.UsuarioCierre = usuario;
            ficha.MotivoCierre = motivo;
            ficha.TraspasoCierre = traspaso;
        }

        // ---------------------------------------------------------------- Consultas

        public async Task<ListadoProductosIncompletosDTO> Listar(bool soloVivos, string almacen, string producto, IPrincipal usuario)
        {
            ContextoUsuarioIncompletos contexto = await ParaConsultar(usuario).ConfigureAwait(false);
            await ComprobarTabla().ConfigureAwait(false);
            string visible = ReglasProductosIncompletos.AlmacenVisible(contexto, almacen);
            List<ProductoIncompletoDTO> fichas = await repositorio.LeerFichas(soloVivos, visible, ReglasProductosIncompletos.Codigo(producto)).ConfigureAwait(false);
            return ReglasProductosIncompletos.Listado(fichas, DateTime.Today);
        }

        public async Task<ProductoIncompletoDTO> Leer(int id, IPrincipal usuario)
        {
            ContextoUsuarioIncompletos contexto = await ParaConsultar(usuario).ConfigureAwait(false);
            await ComprobarTabla().ConfigureAwait(false);
            ProductoIncompletoDTO ficha = await repositorio.LeerFicha(id).ConfigureAwait(false)
                ?? throw Error($"No existe la ficha de producto incompleto {id}.", HttpStatusCode.NotFound);
            string visible = ReglasProductosIncompletos.AlmacenVisible(contexto, null);
            if (visible != null && !string.Equals(visible, ficha.Almacen?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw Error(ReglasProductosIncompletos.MENSAJE_SIN_PERMISO_CONSULTAR, HttpStatusCode.Forbidden);
            }
            return ReglasProductosIncompletos.Completar(ficha, DateTime.Today);
        }

        public async Task<List<PiezaSacadaDTO>> PiezasSacadas(string producto, IPrincipal usuario)
        {
            _ = await ParaConsultar(usuario).ConfigureAwait(false);
            string codigo = ReglasProductosIncompletos.Codigo(producto);
            if (codigo == null)
            {
                throw Error("Diga de qué producto quiere ver las piezas sacadas.");
            }
            await ComprobarTabla().ConfigureAwait(false);
            return await repositorio.LeerPiezasSacadas(codigo).ConfigureAwait(false) ?? new List<PiezaSacadaDTO>();
        }

        public async Task<EtiquetaIncompletoDTO> ImprimirEtiqueta(int id, IPrincipal usuario)
        {
            ContextoUsuarioIncompletos contexto = await ParaOperar(usuario).ConfigureAwait(false);
            await ComprobarTabla().ConfigureAwait(false);
            ProductoIncompletoDTO ficha = await repositorio.LeerFicha(id).ConfigureAwait(false)
                ?? throw Error($"No existe la ficha de producto incompleto {id}.", HttpStatusCode.NotFound);
            ComprobarPermiso(contexto, ficha.Almacen);
            if (!ficha.EstaViva)
            {
                throw Error(ReglasProductosIncompletos.MotivoNoSePuedeCerrar(ficha) + " No hace falta etiqueta.", HttpStatusCode.Conflict);
            }
            return impresora?.Imprimir(ficha, usuario.Identity.Name, false) ?? new EtiquetaIncompletoDTO { Mensaje = "No hay impresora configurada." };
        }

        internal static string Rastro(ResultadoProductoIncompletoDTO resultado) =>
            JsonConvert.SerializeObject(resultado, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
    }
}
