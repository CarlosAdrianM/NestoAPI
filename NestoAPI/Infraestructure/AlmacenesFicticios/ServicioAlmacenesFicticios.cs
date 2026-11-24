using NestoAPI.Infraestructure.Eventos;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.ExtractosProducto;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Seguridad;
using NestoAPI.Infraestructure.Ubicaciones;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.AlmacenesFicticios
{
    public interface IServicioAlmacenesFicticios
    {
        /// <summary>Los almacenes ficticios que el usuario puede ver, con si puede meter y sacar y contra qué almacén.</summary>
        Task<List<AlmacenFicticioDTO>> Listar(IPrincipal usuario);
        /// <summary>Lo que tiene un almacén ficticio (saldo ≠ 0). 403 si no lo puede ver, 404 si no es un ficticio activo.</summary>
        Task<ExistenciasAlmacenFicticioDTO> Existencias(string almacen, IPrincipal usuario);
        /// <summary>Entregar (propio → ficticio) o recoger (ficticio → propio). Con <paramref name="ensayo"/>, comprueba y no escribe.</summary>
        Task<ResultadoTraspasoFicticioDTO> Traspasar(TraspasoAlmacenFicticioDTO peticion, bool ensayo, IPrincipal usuario);
    }

    /// <summary>NestoAPI#894: la campana de Nesto al dueño del almacén ficticio. Nunca lanza.</summary>
    public interface IAvisadorAlmacenFicticio
    {
        Task Avisar(IEnumerable<string> usuarios, NotificacionPushDTO notificacion);
    }

    public class AvisadorAlmacenFicticio : IAvisadorAlmacenFicticio
    {
        private const string DOMINIO = "NUEVAVISION\\";
        private readonly Func<IServicioNotificacionesPush> notificaciones;

        public AvisadorAlmacenFicticio() : this(null)
        {
        }

        internal AvisadorAlmacenFicticio(Func<IServicioNotificacionesPush> notificaciones)
        {
            this.notificaciones = notificaciones ?? (() => new ServicioNotificacionesPush());
        }

        public async Task Avisar(IEnumerable<string> usuarios, NotificacionPushDTO notificacion)
        {
            if (notificacion == null)
            {
                return;
            }
            try
            {
                List<string> destinatarios = (usuarios ?? Enumerable.Empty<string>())
                    .Where(u => !string.IsNullOrWhiteSpace(u))
                    .Select(u => u.Trim())
                    .Select(u => u.Contains("\\") ? u : DOMINIO + u)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                IServicioNotificacionesPush servicio = notificaciones();
                foreach (string usuario in destinatarios)
                {
                    await servicio.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Almacenes ficticios #894] No se ha podido avisar al dueño del almacén: {ex.Message}", ex));
            }
        }
    }

    /// <summary>
    /// NestoAPI#894: consulta y traspasos con los almacenes ficticios. Las reglas son <see cref="ReglasAlmacenesFicticios"/>; la
    /// escritura, en UNA transacción: applock, las dos filas por producto en ExtractoProducto por el núcleo
    /// <see cref="MovimientoTraspasoStockSql"/> (PreExtrProducto + prdExtrProducto por su único punto de llamada, diario «_Ficticio»,
    /// el NºTraspaso de ContadoresGlobales), los huecos de Algete por la puerta única de Ubicaciones y el rastro en Modificaciones.
    /// </summary>
    public class ServicioAlmacenesFicticios : IServicioAlmacenesFicticios
    {
        private readonly IRepositorioAlmacenesFicticios repositorio;
        private readonly IAvisadorAlmacenFicticio avisador;

        public ServicioAlmacenesFicticios(NVEntities db) : this(new RepositorioAlmacenesFicticiosSql(db), new AvisadorAlmacenFicticio())
        {
        }

        internal ServicioAlmacenesFicticios(IRepositorioAlmacenesFicticios repositorio, IAvisadorAlmacenFicticio avisador)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.avisador = avisador;
        }

        private static string Empresa => Constantes.Empresas.EMPRESA_POR_DEFECTO;

        /// <summary>Lo que hace falta del usuario para la regla. Sin token (o un cliente de la tienda), 403.</summary>
        internal async Task<ContextoUsuarioFicticio> Contexto(IPrincipal usuario)
        {
            if (usuario?.Identity?.IsAuthenticated != true)
            {
                throw new NestoBusinessException("Hace falta iniciar sesión.") { StatusCode = HttpStatusCode.Unauthorized };
            }
            if (ValidadorAccesoCliente.EsClienteTienda(usuario))
            {
                throw new NestoBusinessException(ReglasAlmacenesFicticios.MENSAJE_CLIENTE_TIENDA) { StatusCode = HttpStatusCode.Forbidden };
            }
            var contexto = new ContextoUsuarioFicticio
            {
                Usuario = PermisosEventos.SinDominio(usuario.Identity.Name),
                EsDireccion = usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION),
                EsInformatica = usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.INFORMATICA),
                EsAlmacen = usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.ALMACEN),
                EsTiendas = usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.TIENDAS),
                EsTiendaOnline = usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.TIENDA_ON_LINE)
            };
            if (contexto.EsTiendas)
            {
                contexto.AlmacenTienda = GestorFacturacionTienda.NormalizarTienda(
                    await repositorio.LeerParametroPropio(contexto.Usuario, GestorFacturacionTienda.CLAVE_ALMACEN_USUARIO).ConfigureAwait(false));
            }
            contexto.Vendedor = repositorio.VendedorDelUsuario(usuario)?.Trim();
            if (!string.IsNullOrEmpty(contexto.Vendedor))
            {
                contexto.Equipo = new List<string> { contexto.Vendedor };
                contexto.Equipo.AddRange(await repositorio.VendedoresDelEquipo(Empresa, contexto.Vendedor).ConfigureAwait(false) ?? new List<string>());
            }
            return contexto;
        }

        public async Task<List<AlmacenFicticioDTO>> Listar(IPrincipal usuario)
        {
            ContextoUsuarioFicticio contexto = await Contexto(usuario).ConfigureAwait(false);
            List<AlmacenLeido> almacenes = await repositorio.LeerAlmacenes(Empresa).ConfigureAwait(false) ?? new List<AlmacenLeido>();
            List<DuenoAlmacenFicticio> duenos = await repositorio.LeerDuenos().ConfigureAwait(false) ?? new List<DuenoAlmacenFicticio>();
            var lista = new List<AlmacenFicticioDTO>();
            foreach (AlmacenLeido almacen in almacenes.Where(a => a.EsFicticio))
            {
                DecisionAlmacenFicticio decision = ReglasAlmacenesFicticios.Decidir(contexto, almacen.Codigo, duenos);
                if (!decision.PuedeVer)
                {
                    continue;
                }
                lista.Add(new AlmacenFicticioDTO
                {
                    Codigo = almacen.Codigo?.Trim(),
                    Descripcion = almacen.Descripcion?.Trim(),
                    Tipo = decision.Tipo.ToString(),
                    EsDeVendedor = decision.Tipo == TipoAlmacenFicticio.Vendedor,
                    Vendedores = ReglasAlmacenesFicticios.DelAlmacen(almacen.Codigo, duenos)
                        .Select(d => d.Vendedor?.Trim()).Where(v => !string.IsNullOrEmpty(v)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    EsSuyo = decision.EsSuyo,
                    PuedeTraspasar = decision.PuedeTraspasar,
                    AlmacenContra = decision.AlmacenPropio,
                    MotivoNoPuedeTraspasar = decision.PuedeTraspasar ? null : decision.MotivoNoPuedeTraspasar,
                    PermiteNegativo = almacen.PermiteNegativo
                });
            }
            return lista.OrderByDescending(a => a.EsSuyo).ThenBy(a => a.Codigo, StringComparer.Ordinal).ToList();
        }

        public async Task<ExistenciasAlmacenFicticioDTO> Existencias(string almacen, IPrincipal usuario)
        {
            ContextoUsuarioFicticio contexto = await Contexto(usuario).ConfigureAwait(false);
            (AlmacenLeido ficticio, DecisionAlmacenFicticio decision, _, _) = await Resolver(almacen, contexto).ConfigureAwait(false);
            if (!decision.PuedeVer)
            {
                throw new NestoBusinessException(string.Format(ReglasAlmacenesFicticios.MENSAJE_NO_PUEDE_VER, ficticio.Codigo)) { StatusCode = HttpStatusCode.Forbidden };
            }
            List<ExistenciaAlmacenFicticioDTO> productos = await repositorio.LeerExistencias(ficticio.Codigo).ConfigureAwait(false)
                ?? new List<ExistenciaAlmacenFicticioDTO>();
            return new ExistenciasAlmacenFicticioDTO
            {
                Almacen = ficticio.Codigo,
                Descripcion = ficticio.Descripcion?.Trim(),
                Productos = productos
            };
        }

        /// <summary>El ficticio pedido (404 si no lo es o no está activo), la decisión, los almacenes y los dueños.</summary>
        private async Task<(AlmacenLeido Ficticio, DecisionAlmacenFicticio Decision, List<AlmacenLeido> Almacenes, List<DuenoAlmacenFicticio> Duenos)> Resolver(
            string almacen, ContextoUsuarioFicticio contexto)
        {
            string codigo = almacen?.Trim().ToUpperInvariant();
            List<AlmacenLeido> almacenes = await repositorio.LeerAlmacenes(Empresa).ConfigureAwait(false) ?? new List<AlmacenLeido>();
            AlmacenLeido ficticio = almacenes.FirstOrDefault(a => a.EsFicticio && string.Equals(a.Codigo?.Trim(), codigo, StringComparison.OrdinalIgnoreCase));
            if (ficticio == null)
            {
                throw new NestoBusinessException($"{codigo} no es un almacén ficticio activo.") { StatusCode = HttpStatusCode.NotFound };
            }
            ficticio.Codigo = ficticio.Codigo.Trim();
            List<DuenoAlmacenFicticio> duenos = await repositorio.LeerDuenos().ConfigureAwait(false) ?? new List<DuenoAlmacenFicticio>();
            return (ficticio, ReglasAlmacenesFicticios.Decidir(contexto, ficticio.Codigo, duenos), almacenes, duenos);
        }

        public async Task<ResultadoTraspasoFicticioDTO> Traspasar(TraspasoAlmacenFicticioDTO peticion, bool ensayo, IPrincipal usuario)
        {
            ContextoUsuarioFicticio contexto = await Contexto(usuario).ConfigureAwait(false);
            string invalida = ReglasAlmacenesFicticios.MotivoPeticionNoValida(peticion);
            if (invalida != null)
            {
                throw new NestoBusinessException(invalida);
            }
            (AlmacenLeido ficticio, DecisionAlmacenFicticio decision, List<AlmacenLeido> almacenes, List<DuenoAlmacenFicticio> duenos) =
                await Resolver(peticion.AlmacenFicticio, contexto).ConfigureAwait(false);
            if (!decision.PuedeTraspasar)
            {
                throw new NestoBusinessException(decision.MotivoNoPuedeTraspasar) { StatusCode = HttpStatusCode.Forbidden };
            }
            AlmacenLeido propio = almacenes.FirstOrDefault(a => !a.EsFicticio && string.Equals(a.Codigo?.Trim(), decision.AlmacenPropio, StringComparison.OrdinalIgnoreCase));
            if (propio == null)
            {
                throw new NestoBusinessException($"El almacén {decision.AlmacenPropio} no existe o no está activo.");
            }
            string codigoPropio = propio.Codigo.Trim();
            string sentido = SentidosTraspasoFicticio.Normalizar(peticion.Sentido);
            bool entregar = sentido == SentidosTraspasoFicticio.ENTREGAR;
            Dictionary<string, int> unidades = ReglasAlmacenesFicticios.UnidadesPorProducto(peticion.Lineas);

            Dictionary<string, ProductoTraspasoFicticio> productos = await repositorio.LeerProductos(unidades.Keys).ConfigureAwait(false)
                ?? new Dictionary<string, ProductoTraspasoFicticio>(StringComparer.OrdinalIgnoreCase);
            List<string> motivos = ReglasAlmacenesFicticios.MotivosProductos(unidades.Keys, productos, sentido);
            if (motivos.Any())
            {
                throw new NestoBusinessException(string.Join(" ", motivos));
            }

            AlmacenLeido origen = entregar ? propio : ficticio;
            Dictionary<string, int> stockOrigen = await repositorio.LeerStock(origen.Codigo.Trim(), unidades.Keys).ConfigureAwait(false);
            motivos = ReglasAlmacenesFicticios.MotivosStock(unidades, stockOrigen, origen.PermiteNegativo, origen.Codigo.Trim(), !entregar, productos);
            if (motivos.Any())
            {
                throw new NestoBusinessException(string.Join(" ", motivos));
            }

            // Algete: al entregar, de qué hueco sale cada línea (propuesto si no viene)
            bool ConHuecos(string producto) => propio.ControlUbicaciones && productos.TryGetValue(producto, out ProductoTraspasoFicticio p) && p.Ubicar;
            List<LineaTraspasoFicticioPlan> plan;
            if (entregar && propio.ControlUbicaciones && unidades.Keys.Any(ConHuecos))
            {
                List<HuecoLibreFicticio> huecos = await repositorio.LeerHuecosLibres(codigoPropio, unidades.Keys.Where(ConHuecos)).ConfigureAwait(false);
                plan = ReglasAlmacenesFicticios.AsignarHuecos(peticion.Lineas, huecos, ConHuecos, codigoPropio, out motivos);
                if (motivos.Any())
                {
                    throw new NestoBusinessException(string.Join(" ", motivos));
                }
            }
            else
            {
                plan = ReglasAlmacenesFicticios.AsignarHuecos(peticion.Lineas, null, null, codigoPropio, out _);
            }

            Dictionary<string, int> saldoAntes = entregar
                ? await repositorio.LeerStock(ficticio.Codigo, unidades.Keys).ConfigureAwait(false)
                : stockOrigen;

            if (ensayo)
            {
                return Resultado(true, null, sentido, codigoPropio, ficticio.Codigo, plan, productos, ConHuecos,
                    ReglasAlmacenesFicticios.SaldoPrevisto(sentido, unidades, saldoAntes));
            }

            string quien = UsuarioAuditoriaHelper.ParaAuditoria(usuario.Identity.Name);
            ResultadoTraspasoFicticioDTO hecho = await repositorio.EnTransaccion(async t =>
            {
                await t.Bloquear(Empresa).ConfigureAwait(false);
                if (peticion.Clave.HasValue)
                {
                    ResultadoTraspasoFicticioDTO repetido = await t.LeerPorClave(peticion.Clave.Value).ConfigureAwait(false);
                    if (repetido != null)
                    {
                        repetido.Mensaje = "Este traspaso ya estaba hecho: " + repetido.Mensaje;
                        return repetido;
                    }
                }
                // Lo que había puede haber cambiado desde la comprobación: se vuelve a mirar con el bloqueo puesto
                Dictionary<string, int> stockAhora = await t.LeerStock(origen.Codigo.Trim(), unidades.Keys).ConfigureAwait(false);
                List<string> faltas = ReglasAlmacenesFicticios.MotivosStock(unidades, stockAhora, origen.PermiteNegativo, origen.Codigo.Trim(), !entregar, productos);
                if (faltas.Any())
                {
                    throw new NestoBusinessException(string.Join(" ", faltas) + " El stock ha cambiado mientras tanto: no se ha hecho nada.")
                    {
                        StatusCode = HttpStatusCode.Conflict
                    };
                }

                int traspaso = await t.SiguienteTraspaso().ConfigureAwait(false);
                _ = await t.Contabilizar(traspaso,
                    ReglasAlmacenesFicticios.LineasExtracto(sentido, codigoPropio, ficticio.Codigo, unidades, productos), quien).ConfigureAwait(false);

                foreach (LineaTraspasoFicticioPlan linea in plan)
                {
                    if (!ConHuecos(linea.Producto))
                    {
                        continue;
                    }
                    if (entregar)
                    {
                        await t.SacarDeHueco(codigoPropio, linea.Producto,
                            HuecoUbicacion.De(linea.Hueco.Pasillo, linea.Hueco.Fila, linea.Hueco.Columna), linea.Cantidad, traspaso, quien).ConfigureAwait(false);
                    }
                }
                if (!entregar)
                {
                    foreach (KeyValuePair<string, int> u in unidades.Where(u => ConHuecos(u.Key)))
                    {
                        await t.DejarPendienteDeUbicar(codigoPropio, u.Key, u.Value, traspaso, quien).ConfigureAwait(false);
                    }
                }

                Dictionary<string, int> saldoDespues = await t.LeerStock(ficticio.Codigo, unidades.Keys).ConfigureAwait(false);
                ResultadoTraspasoFicticioDTO resultado = Resultado(false, traspaso, sentido, codigoPropio, ficticio.Codigo, plan, productos, ConHuecos,
                    unidades.Keys.ToDictionary(k => k, k => saldoDespues != null && saldoDespues.TryGetValue(k, out int s) ? s : 0, StringComparer.OrdinalIgnoreCase));
                string anterior = peticion.Clave?.ToString("D", CultureInfo.InvariantCulture) ?? $"Traspaso {traspaso}";
                await t.ApuntarRastro(anterior, Rastro(resultado, quien, peticion.Comentario), quien).ConfigureAwait(false);
                return resultado;
            }).ConfigureAwait(false);

            if (avisador != null && hecho?.NumeroTraspaso != null)
            {
                List<string> aQuien = ReglasAlmacenesFicticios.DelAlmacen(ficticio.Codigo, duenos)
                    .Select(d => d.Usuario?.Trim())
                    .Where(u => !string.IsNullOrEmpty(u) && !string.Equals(u, contexto.Usuario, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (aQuien.Any())
                {
                    await avisador.Avisar(aQuien, ReglasAlmacenesFicticios.Notificacion(hecho, contexto.Usuario)).ConfigureAwait(false);
                }
            }
            return hecho;
        }

        private static ResultadoTraspasoFicticioDTO Resultado(bool ensayo, int? traspaso, string sentido, string propio, string ficticio,
            List<LineaTraspasoFicticioPlan> plan, Dictionary<string, ProductoTraspasoFicticio> productos, Func<string, bool> conHuecos,
            Dictionary<string, int> saldos)
        {
            bool recogerConHuecos = sentido == SentidosTraspasoFicticio.RECOGER;
            var resultado = new ResultadoTraspasoFicticioDTO
            {
                Ensayo = ensayo,
                NumeroTraspaso = traspaso,
                AlmacenFicticio = ficticio,
                AlmacenPropio = propio,
                Sentido = sentido,
                Lineas = plan.Select(l => new LineaTraspasoFicticioHechaDTO
                {
                    Producto = l.Producto,
                    Nombre = productos.TryGetValue(l.Producto, out ProductoTraspasoFicticio p) ? p.Nombre?.Trim() : null,
                    Cantidad = l.Cantidad,
                    Pasillo = l.Hueco?.Pasillo,
                    Fila = l.Hueco?.Fila,
                    Columna = l.Hueco?.Columna,
                    Hueco = l.Hueco?.Texto,
                    HuecoPropuesto = l.Hueco != null && l.HuecoPropuesto,
                    PendienteDeUbicar = recogerConHuecos && conHuecos(l.Producto) ? l.Cantidad : 0
                }).ToList(),
                Saldos = (saldos ?? new Dictionary<string, int>()).Select(s => new SaldoAlmacenFicticioDTO
                {
                    Producto = s.Key,
                    Nombre = productos.TryGetValue(s.Key, out ProductoTraspasoFicticio p) ? p.Nombre?.Trim() : null,
                    Unidades = s.Value
                }).ToList()
            };
            resultado.UnidadesPendientesDeUbicar = resultado.Lineas.Sum(l => l.PendienteDeUbicar);
            resultado.Mensaje = ReglasAlmacenesFicticios.Mensaje(sentido, propio, ficticio, plan.Sum(l => l.Cantidad),
                plan.Select(l => l.Producto).Distinct(StringComparer.OrdinalIgnoreCase).Count(), traspaso, resultado.UnidadesPendientesDeUbicar);
            return resultado;
        }

        /// <summary>Modificaciones.Nuevo: el resultado (para devolverlo igual con la misma clave), quién y el comentario.</summary>
        internal static string Rastro(ResultadoTraspasoFicticioDTO resultado, string usuario, string comentario)
        {
            var rastro = JsonConvert.DeserializeObject<RastroTraspasoFicticio>(JsonConvert.SerializeObject(resultado));
            rastro.Usuario = usuario;
            rastro.Comentario = string.IsNullOrWhiteSpace(comentario) ? null : comentario.Trim();
            return JsonConvert.SerializeObject(rastro, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
        }

        private class RastroTraspasoFicticio : ResultadoTraspasoFicticioDTO
        {
            public string Usuario { get; set; }
            public string Comentario { get; set; }
        }
    }
}
