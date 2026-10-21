using ModeloSugerenciasProductos.Entrenamiento;
using ModeloSugerenciasProductos.Features;
using ModeloSugerenciasProductos.Reglas;
using NestoAPI.Infraestructure.CorreosPostCompra;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Compra = ModeloSugerenciasProductos.Datos.Compra;
using FichaProducto = ModeloSugerenciasProductos.Datos.FichaProducto;

namespace NestoAPI.Infraestructure.SugerenciasProductos
{
    public interface IServicioSugerenciasProductos
    {
        /// <exception cref="SugerenciasProductosException">Petición que no vale (400).</exception>
        Task<SugerenciasProductosDTO> Calcular(CalcularSugerenciasDTO peticion, string usuario);
        /// <exception cref="SugerenciasProductosException">Respuesta que no vale (400) o sugerencia que no existe (404).</exception>
        Task<ResultadoRespuestaSugerenciaDTO> Responder(int idSugerencia, RespuestaSugerenciaDTO respuesta);
        /// <summary>Enlaza la sesión con el pedido guardado y marca qué sugerencias entraron. Nunca lanza.</summary>
        Task EnlazarPedido(PedidoVentaDTO pedido);
    }

    /// <summary>
    /// NestoAPI#670 (corte 1 de la épica #669): las sugerencias de la plantilla de venta y su registro.
    /// <para>Estabilidad: las sugerencias de la sesión que nadie ha tocado y siguen valiendo (filtros y stock) se mantienen en su
    /// hueco; solo se rellenan los huecos (hasta <see cref="MotorSugerenciasProductos.SUGERENCIAS_VISIBLES"/>) con productos que
    /// no se hayan enseñado ya en la sesión. Las que dejan de valer (p. ej. porque el producto ya está en el carrito) se dejan de
    /// enseñar, pero su fila queda sin respuesta: no cuentan como ofrecidas.</para>
    /// <para>Sin las tablas del script Issue670 todo devuelve vacío (Calcular) o 404 (Responder), y el enlace con el pedido no
    /// hace nada.</para>
    /// <para>NestoAPI#671 (corte 2c): con el modelo 1 (<see cref="IPredictorSugerenciasProductos"/>), los huecos se llenan por
    /// P1; sin él o si falla, reglas-1. NestoAPI#682: en Calcular, una visible sin contestar puede ceder su hueco a una nueva
    /// claramente mejor (como mucho una por llamada). NestoAPI#669: dos «🚫 Quitar» de una familia la excluyen para el cliente.</para>
    /// </summary>
    public class ServicioSugerenciasProductos : IServicioSugerenciasProductos
    {
        private readonly IRepositorioSugerenciasProductos repositorio;
        private readonly Func<DateTime> ahora;
        private readonly Action<Exception> registrarError;
        private readonly IFotosProductoAlmacen fotos;
        private readonly IPredictorSugerenciasProductos predictor;

        /// <param name="fotos">Nesto#537: las fotos de la tienda (las de Ariadna, con caché). Null: las fichas van sin foto.</param>
        /// <param name="predictor">NestoAPI#671 (corte 2c): el modelo 1. Null: solo reglas («reglas-1»), como en el corte 1.</param>
        public ServicioSugerenciasProductos(IRepositorioSugerenciasProductos repositorio, Func<DateTime> ahora = null,
            Action<Exception> registrarError = null, IFotosProductoAlmacen fotos = null, IPredictorSugerenciasProductos predictor = null)
        {
            this.repositorio = repositorio;
            this.ahora = ahora ?? (() => DateTime.Now);
            this.registrarError = registrarError ?? ElmahHelper.Log;
            this.fotos = fotos;
            this.predictor = predictor;
        }

        /// <summary>
        /// Lo que llaman PostPedidoVenta y PutPedidoVenta después de guardar. Sin sesión de sugerencias no hace nada (ni abre
        /// conexión); con ella, nunca rompe el guardado: los errores van a ELMAH.
        /// <para>NestoAPI#687: fuera de la transacción ambiente (TransactionScope Suppress). PutPedidoVenta también se ejecuta
        /// DENTRO del TransactionScope de UnirPedidos; el registro de sugerencias no forma parte del pedido (solo toca sus
        /// tablas) y su NVEntities propio no debe alistarse: con dos conexiones a la vez la transacción escalaría a MSDTC
        /// (deshabilitado) y la unión fallaría entera. La unión vuelve a enlazar la sesión al terminar
        /// (<see cref="EnlazarUnionSinFallar"/>).</para>
        /// </summary>
        public static Task EnlazarPedidoSinFallar(PedidoVentaDTO pedido)
            => EnlazarPedidoSinFallar(pedido, EnlazarConContextoPropio, ElmahHelper.Log);

        private static async Task EnlazarConContextoPropio(PedidoVentaDTO pedido)
        {
            using (NVEntities db = new NVEntities())
            {
                await new ServicioSugerenciasProductos(new RepositorioSugerenciasProductosSql(db)).EnlazarPedido(pedido).ConfigureAwait(false);
            }
        }

        /// <param name="enlazar">El enlace de verdad (sustituible en los tests).</param>
        internal static async Task EnlazarPedidoSinFallar(PedidoVentaDTO pedido, Func<PedidoVentaDTO, Task> enlazar, Action<Exception> registrarError)
        {
            if (pedido?.IdSesionSugerencias == null || pedido.IdSesionSugerencias.Value == Guid.Empty)
            {
                return;
            }
            try
            {
                using (var sinTransaccion = new System.Transactions.TransactionScope(System.Transactions.TransactionScopeOption.Suppress,
                    System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
                {
                    await enlazar(pedido).ConfigureAwait(false);
                    sinTransaccion.Complete();
                }
            }
            catch (Exception ex)
            {
                registrarError(new Exception($"[Sugerencias #670] No se ha podido enlazar la sesión {pedido.IdSesionSugerencias} con el pedido {pedido.numero}: {ex.Message}", ex));
            }
        }

        /// <summary>
        /// NestoAPI#670 (Nesto#537): al ampliar un pedido pendiente desde la plantilla (UnirPedidos), la sesión de sugerencias
        /// viene en la ampliación; se enlaza con el pedido que queda tras la unión y se marca qué sugerencias entraron. Se
        /// llama fuera del TransactionScope de la unión y nunca rompe la unión: los errores van a ELMAH.
        /// </summary>
        /// <param name="enlazar">Para los tests; por defecto, <see cref="EnlazarPedidoSinFallar"/>.</param>
        public static async Task EnlazarUnionSinFallar(PedidoVentaDTO pedidoUnido, PedidoVentaDTO ampliacion,
            Func<PedidoVentaDTO, Task> enlazar = null, Action<Exception> registrarError = null)
        {
            Guid? idSesion = ampliacion?.IdSesionSugerencias;
            if (pedidoUnido == null || idSesion == null || idSesion.Value == Guid.Empty)
            {
                return;
            }
            pedidoUnido.IdSesionSugerencias = idSesion;
            try
            {
                await (enlazar ?? EnlazarPedidoSinFallar)(pedidoUnido).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                (registrarError ?? ElmahHelper.Log)(new Exception($"[Sugerencias #670] No se ha podido enlazar la sesión {idSesion} con el pedido unido {pedidoUnido.numero}: {ex.Message}", ex));
            }
        }

        public async Task<SugerenciasProductosDTO> Calcular(CalcularSugerenciasDTO peticion, string usuario)
        {
            if (peticion == null || string.IsNullOrWhiteSpace(peticion.Cliente))
            {
                throw new SugerenciasProductosException(false, "Falta el cliente.");
            }
            string aplicacion = Aplicacion(peticion.Aplicacion);
            if (aplicacion == null)
            {
                throw new SugerenciasProductosException(false, "La aplicación tiene que ser «Nesto» o «NestoApp».");
            }
            string empresa = string.IsNullOrWhiteSpace(peticion.Empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : peticion.Empresa.Trim();
            string cliente = peticion.Cliente.Trim();
            Guid idSesion = peticion.IdSesion.HasValue && peticion.IdSesion.Value != Guid.Empty ? peticion.IdSesion.Value : Guid.NewGuid();
            var resultado = new SugerenciasProductosDTO { IdSesion = idSesion };

            if (!await repositorio.ExistenTablas().ConfigureAwait(false))
            {
                return resultado;
            }

            Dictionary<string, int> carrito = Carrito(peticion.Productos);
            SesionSugerencias sesion = await repositorio.LeerSesion(idSesion).ConfigureAwait(false);
            if (sesion != null && (!Igual(sesion.Empresa, empresa) || !Igual(sesion.Cliente, cliente)))
            {
                // La plantilla ha cambiado de cliente: lo de antes no le vale a este.
                sesion = null;
                idSesion = Guid.NewGuid();
                resultado.IdSesion = idSesion;
            }
            if (sesion == null)
            {
                sesion = new SesionSugerencias
                {
                    Id = idSesion,
                    Empresa = empresa,
                    Cliente = cliente,
                    Contacto = Limpio(peticion.Contacto),
                    Vendedor = Limpio(peticion.Vendedor),
                    Usuario = usuario,
                    Aplicacion = aplicacion,
                    FechaInicio = ahora()
                };
                await repositorio.CrearSesion(sesion).ConfigureAwait(false);
            }
            sesion.Contacto = Limpio(peticion.Contacto) ?? sesion.Contacto;
            sesion.Vendedor = Limpio(peticion.Vendedor) ?? sesion.Vendedor;
            sesion.Carrito = SugerenciasProductosSql.TextoCarrito(carrito);
            await repositorio.ActualizarSesion(idSesion, sesion.Contacto, sesion.Vendedor, sesion.Carrito).ConfigureAwait(false);

            List<SugerenciaMostrada> mostradas = await repositorio.LeerMostradas(idSesion).ConfigureAwait(false);
            // NestoAPI#682: solo aquí (cambio de carrito) se sustituye una visible sin contestar; al responder, no (el cliente
            // solo pinta la sustituta en el hueco contestado).
            resultado.Sugerencias = (await Rellenar(sesion, carrito, mostradas, permitirSustitucion: true).ConfigureAwait(false)).Visibles;
            return resultado;
        }

        public async Task<ResultadoRespuestaSugerenciaDTO> Responder(int idSugerencia, RespuestaSugerenciaDTO peticion)
        {
            string respuesta = Respuesta(peticion?.Respuesta);
            if (respuesta == null)
            {
                throw new SugerenciasProductosException(false, "La respuesta tiene que ser «Anadido», «NoLeInteresa» o «DescartadoVendedor».");
            }
            if (!await repositorio.ExistenTablas().ConfigureAwait(false))
            {
                throw new SugerenciasProductosException(true, "Las sugerencias de productos todavía no están disponibles.");
            }
            SugerenciaMostrada mostrada = await repositorio.LeerMostrada(idSugerencia).ConfigureAwait(false)
                ?? throw new SugerenciasProductosException(true, $"No existe la sugerencia {idSugerencia}.");

            await GuardarRespuesta(idSugerencia, respuesta).ConfigureAwait(false);
            var resultado = new ResultadoRespuestaSugerenciaDTO { IdSugerencia = idSugerencia, Respuesta = respuesta };
            if (!RespuestasSugerencia.ExcluyenSesentaDias(respuesta))
            {
                return resultado;
            }

            SesionSugerencias sesion = await repositorio.LeerSesion(mostrada.SesionId).ConfigureAwait(false);
            if (sesion == null)
            {
                return resultado;
            }
            Dictionary<string, int> carrito;
            if (peticion.Productos != null)
            {
                carrito = Carrito(peticion.Productos);
                sesion.Carrito = SugerenciasProductosSql.TextoCarrito(carrito);
                await repositorio.ActualizarSesion(sesion.Id, sesion.Contacto, sesion.Vendedor, sesion.Carrito).ConfigureAwait(false);
            }
            else
            {
                carrito = SugerenciasProductosSql.LeerCarrito(sesion.Carrito);
            }
            List<SugerenciaMostrada> mostradas = await repositorio.LeerMostradas(sesion.Id).ConfigureAwait(false);
            foreach (SugerenciaMostrada m in mostradas.Where(m => m.Id == idSugerencia))
            {
                m.Respuesta = respuesta; // por si la lectura no ve todavía la respuesta recién guardada
            }
            ResultadoRelleno relleno = await Rellenar(sesion, carrito, mostradas, permitirSustitucion: false).ConfigureAwait(false);
            resultado.Sugerencias = relleno.Visibles;
            List<SugerenciaProductoDTO> nuevas = relleno.Visibles.Where(v => relleno.Nuevas.Contains(v.IdSugerencia)).ToList();
            resultado.Sustituta = nuevas.FirstOrDefault(v => v.Posicion == mostrada.Posicion) ?? nuevas.FirstOrDefault();
            return resultado;
        }

        public async Task EnlazarPedido(PedidoVentaDTO pedido)
        {
            if (pedido?.IdSesionSugerencias == null || pedido.IdSesionSugerencias.Value == Guid.Empty)
            {
                return;
            }
            Guid idSesion = pedido.IdSesionSugerencias.Value;
            try
            {
                if (!await repositorio.ExistenTablas().ConfigureAwait(false))
                {
                    return;
                }
                SesionSugerencias sesion = await repositorio.LeerSesion(idSesion).ConfigureAwait(false);
                if (sesion == null)
                {
                    return;
                }
                await repositorio.EnlazarPedido(idSesion, pedido.numero).ConfigureAwait(false);
                Dictionary<string, int> lineas = LineasPedido(pedido);
                foreach (SugerenciaMostrada mostrada in await repositorio.LeerMostradas(idSesion).ConfigureAwait(false))
                {
                    Tuple<bool, int?> entrada = MotorSugerenciasProductos.EntradaEnPedido(mostrada.Producto, lineas);
                    if (mostrada.EntroEnPedido != entrada.Item1 || mostrada.CantidadEnPedido != entrada.Item2)
                    {
                        await repositorio.MarcarEntradaEnPedido(mostrada.Id, entrada.Item1, entrada.Item2).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                registrarError(new Exception($"[Sugerencias #670] No se ha podido enlazar la sesión {idSesion} con el pedido {pedido.numero}: {ex.Message}", ex));
            }
        }

        /// <summary>
        /// Nesto#537: «DescartadoVendedor» va aparte de «NoLeInteresa» (no es un rechazo del cliente). Si el CK de Respuesta
        /// todavía no lo admite (sin el script Issue670b), se guarda provisionalmente como «NoLeInteresa» con la marca en el
        /// Texto: excluye igual 60 días y el script lo reclasifica. Nunca falla por el CK.
        /// </summary>
        private async Task GuardarRespuesta(int idSugerencia, string respuesta)
        {
            if (respuesta == RespuestasSugerencia.DESCARTADO_VENDEDOR && !await repositorio.AdmiteDescartadoVendedor().ConfigureAwait(false))
            {
                await repositorio.GuardarDescartadoVendedorProvisional(idSugerencia).ConfigureAwait(false);
                return;
            }
            await repositorio.GuardarRespuesta(idSugerencia, respuesta).ConfigureAwait(false);
        }

        internal class ResultadoRelleno
        {
            public List<SugerenciaProductoDTO> Visibles { get; set; } = new List<SugerenciaProductoDTO>();
            public HashSet<int> Nuevas { get; set; } = new HashSet<int>();
        }

        /// <summary>NestoAPI#671 (corte 2c): un candidato puntuado por el modelo, con lo que se enseña si sale.</summary>
        internal class CandidatoModelo
        {
            public CandidatoSugerencia Candidato { get; set; }
            public float P1 { get; set; }
            public string Fuentes { get; set; }
            public bool EsHabitual { get; set; }
            public string Familia { get; set; }
        }

        /// <summary>NestoAPI#671 (corte 2c): lo que sale del modelo para una llamada.</summary>
        internal class PuntuacionModelo
        {
            public string Version { get; set; }
            /// <summary>Los candidatos nuevos que valen (sin el stock), de más a menos P1.</summary>
            public List<CandidatoModelo> Orden { get; set; } = new List<CandidatoModelo>();
            /// <summary>P1 de ahora (con el carrito de ahora) de las visibles sin contestar.</summary>
            public Dictionary<string, float> P1Visibles { get; set; } = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Las que se ven: las que se mantienen y las nuevas que llenan los huecos (y se registran).
        /// <para>NestoAPI#671 (corte 2c): con el modelo, los huecos se llenan por P1 (todas las fuentes juntas, sin diversidad:
        /// <see cref="MotorSugerenciasProductos.APLICAR_DIVERSIDAD"/>) y, en las sesiones que exploran, el hueco 3 se elige al azar
        /// entre los puestos 4 a 15. Sin modelo (o si falla), las reglas de siempre.</para>
        /// <para>NestoAPI#682 (<paramref name="permitirSustitucion"/>, solo Calcular): como mucho una visible sin contestar se
        /// cambia, en su mismo hueco, por una nueva claramente mejor; la sustituida queda como «Sustituida».</para>
        /// </summary>
        private async Task<ResultadoRelleno> Rellenar(SesionSugerencias sesion, Dictionary<string, int> carrito, List<SugerenciaMostrada> mostradas,
            bool permitirSustitucion)
        {
            DateTime hoy = ahora().Date;
            string empresa = sesion.Empresa?.Trim();
            string cliente = sesion.Cliente?.Trim();
            var enCarrito = new HashSet<string>(carrito.Keys, StringComparer.OrdinalIgnoreCase);
            mostradas = mostradas ?? new List<SugerenciaMostrada>();

            // Lo que hace falta leer
            DatosGrupoVendedor datosVendedor = await repositorio.LeerDatosGrupoVendedor(empresa, cliente, sesion.Contacto, sesion.Vendedor).ConfigureAwait(false);
            FiltroGrupoVendedor filtro = MotorSugerenciasProductos.GrupoVendedor(sesion.Vendedor, datosVendedor);
            List<CompraDiaReposicion> compras = await repositorio.LeerComprasCliente(empresa, cliente,
                hoy.AddMonths(-MotorSugerenciasProductos.MESES_HISTORIAL), hoy.AddDays(1)).ConfigureAwait(false) ?? new List<CompraDiaReposicion>();
            HashSet<string> pendientes = await repositorio.LeerPendientesCliente(empresa, cliente).ConfigureAwait(false)
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<RechazoCliente> rechazos = (await repositorio.LeerRechazados(empresa, cliente,
                hoy.AddDays(-MotorSugerenciasProductos.DIAS_SIN_REPETIR_RECHAZO)).ConfigureAwait(false) ?? new List<RechazoCliente>())
                .Where(r => r != null && !string.IsNullOrWhiteSpace(r.Producto)).ToList();
            var rechazados = new HashSet<string>(rechazos.Select(r => r.Producto.Trim()), StringComparer.OrdinalIgnoreCase);
            foreach (SugerenciaMostrada rechazada in mostradas.Where(m => RespuestasSugerencia.ExcluyenSesentaDias(m.Respuesta)))
            {
                _ = rechazados.Add(rechazada.Producto.Trim());
            }
            List<ParCompradoJunto> pares = enCarrito.Count == 0 ? new List<ParCompradoJunto>()
                : await repositorio.LeerPares(enCarrito).ConfigureAwait(false) ?? new List<ParCompradoJunto>();
            List<CandidatoPotenciar> potenciar = await repositorio.LeerPotenciar(empresa, hoy).ConfigureAwait(false) ?? new List<CandidatoPotenciar>();
            List<AfinidadCliente> afinidades = await repositorio.LeerAfinidad(cliente).ConfigureAwait(false) ?? new List<AfinidadCliente>();

            var comprados = new HashSet<string>(compras.Where(c => c.Producto != null).Select(c => c.Producto.Trim()), StringComparer.OrdinalIgnoreCase);
            // NestoAPI#682: las sustituidas antes del script Issue682 siguen sin respuesta, pero ya no se enseñan
            List<SugerenciaMostrada> sinResponder = mostradas
                .Where(m => m.Respuesta == null && !RespuestasSugerencia.EsSustituidaProvisional(m.Respuesta, m.Texto))
                .OrderBy(m => m.Posicion).ThenBy(m => m.Id).ToList();
            IEnumerable<string> productosConFicha = comprados
                .Concat(enCarrito)
                .Concat(pares.Select(p => p.ProductoA)).Concat(pares.Select(p => p.ProductoB))
                .Concat(potenciar.Select(p => p.Producto))
                .Concat(afinidades.Select(a => a.Producto))
                .Concat(sinResponder.Select(m => m.Producto))
                .Concat(mostradas.Where(m => RespuestasSugerencia.EsQuitada(m.Respuesta, m.Texto)).Select(m => m.Producto));
            Dictionary<string, FichaProductoSugerencia> fichas = await repositorio.LeerFichas(empresa, productosConFicha).ConfigureAwait(false)
                ?? new Dictionary<string, FichaProductoSugerencia>(StringComparer.OrdinalIgnoreCase);
            fichas = new Dictionary<string, FichaProductoSugerencia>(fichas, StringComparer.OrdinalIgnoreCase);

            Tuple<string, string> perfil = MotorSugerenciasProductos.Perfil(compras, fichas);
            Dictionary<string, int> clientesPerfil = perfil == null ? new Dictionary<string, int>()
                : await repositorio.LeerClientesPerfil(perfil.Item1, perfil.Item2).ConfigureAwait(false) ?? new Dictionary<string, int>();
            clientesPerfil = new Dictionary<string, int>(clientesPerfil, StringComparer.OrdinalIgnoreCase);

            // NestoAPI#669: dos «🚫 Quitar» de la misma familia para este cliente la excluyen entera (en todas las fuentes, en
            // las sustitutas de #682 y en la exploración: todo pasa por Vale)
            HashSet<string> familiasExcluidas = MotorSugerenciasProductos.FamiliasQuitadas(rechazos, mostradas, fichas);

            bool Vale(string producto) => MotorSugerenciasProductos.MotivoDescarte(producto,
                fichas.TryGetValue(producto?.Trim() ?? string.Empty, out FichaProductoSugerencia f) ? f : null,
                filtro, enCarrito, pendientes, rechazados, familiasExcluidas) == null;

            // Lo ya enseñado en la sesión no vuelve como nuevo (se haya respondido o no)
            var yaEnsenados = new HashSet<string>(mostradas.Select(m => m.Producto.Trim()), StringComparer.OrdinalIgnoreCase);
            List<CandidatoSugerencia> todos = MotorSugerenciasProductos.CompradoJunto(pares, enCarrito, fichas)
                .Concat(MotorSugerenciasProductos.LeToca(compras, hoy))
                .Concat(MotorSugerenciasProductos.Potenciar(potenciar, clientesPerfil, comprados))
                .Concat(MotorSugerenciasProductos.Parecidos(afinidades, comprados))
                .ToList();
            // NestoAPI#671: todas las fuentes que proponen cada producto (para el modelo; al vendedor solo se le enseña una)
            Dictionary<string, string> fuentes = MotorSugerenciasProductos.Fuentes(todos);
            List<CandidatoSugerencia> candidatos = todos
                .Where(c => !yaEnsenados.Contains(c.Producto) && Vale(c.Producto))
                .GroupBy(c => c.Motivo)
                .SelectMany(g => g.OrderByDescending(c => c.Puntuacion).ThenBy(c => c.Producto, StringComparer.OrdinalIgnoreCase)
                    .Take(MotorSugerenciasProductos.CANDIDATOS_POR_FUENTE))
                .ToList();
            List<SugerenciaMostrada> quedan = sinResponder
                .Where(m => Vale(m.Producto))
                .GroupBy(m => m.Producto.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            // NestoAPI#671 (corte 2c): el modelo puntúa todas las candidatas (y las visibles); si no hay o falla, reglas-1
            PuntuacionModelo modelo = await PuntuarConModelo(hoy, cliente, empresa, compras, enCarrito, pares, afinidades, fichas, perfil,
                clientesPerfil, todos, fuentes, quedan, yaEnsenados, Vale).ConfigureAwait(false);

            // El stock (o la fecha de llegada), de una vez, solo para lo que puede salir
            IEnumerable<string> paraStock = modelo != null
                ? modelo.Orden.Take(MotorSugerenciasProductos.CANDIDATOS_MODELO_CON_STOCK).Select(c => c.Candidato.Producto)
                : candidatos.Select(c => c.Producto);
            HashSet<string> vendibles = await repositorio.LeerVendibles(empresa,
                quedan.Select(m => m.Producto).Concat(paraStock)).ConfigureAwait(false)
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            vendibles = new HashSet<string>(vendibles, StringComparer.OrdinalIgnoreCase);

            List<SugerenciaMostrada> visibles = quedan
                .Where(m => vendibles.Contains(m.Producto.Trim()))
                .Take(MotorSugerenciasProductos.MAXIMO_SUGERENCIAS)
                .ToList();
            var mantenidas = new HashSet<int>(visibles.Select(v => v.Id));
            int huecos = Math.Max(0, MotorSugerenciasProductos.SUGERENCIAS_VISIBLES - visibles.Count);
            var excluidos = new HashSet<string>(yaEnsenados.Concat(enCarrito), StringComparer.OrdinalIgnoreCase);
            bool explora = MotorSugerenciasProductos.EsSesionDeExploracion(sesion.Id);
            var resultado = new ResultadoRelleno();
            var ocupadas = visibles.Select(v => (int)v.Posicion).ToList();
            int tamanoCarrito = enCarrito.Count;

            async Task<SugerenciaMostrada> Registrar(CandidatoSugerencia elegida, int posicion, string fuentesProducto, bool exploracion, string version)
            {
                var nueva = new SugerenciaMostrada
                {
                    SesionId = sesion.Id,
                    Producto = elegida.Producto,
                    Motivo = elegida.Motivo,
                    Puntuacion = elegida.Puntuacion,
                    Posicion = (byte)posicion,
                    VersionMotor = version,
                    Texto = elegida.Texto,
                    FechaMostrada = ahora(),
                    Fuentes = fuentesProducto,
                    TamanoCarrito = tamanoCarrito,
                    Exploracion = exploracion
                };
                nueva.Id = await repositorio.InsertarMostrada(nueva).ConfigureAwait(false);
                ocupadas.Add(posicion);
                visibles.Add(nueva);
                _ = resultado.Nuevas.Add(nueva.Id);
                return nueva;
            }

            if (modelo != null)
            {
                List<CandidatoModelo> orden = modelo.Orden.Where(c => vendibles.Contains(c.Candidato.Producto) && !excluidos.Contains(c.Candidato.Producto)).ToList();
                if (MotorSugerenciasProductos.APLICAR_DIVERSIDAD)
                {
                    orden = ModeloSugerenciasProductos.Evaluacion.Diversidad.Aplicar(orden, c => c.EsHabitual, c => c.Familia, orden.Count);
                }
                // Los puestos de la exploración son los del orden de esta llamada (antes de elegir nada)
                List<CandidatoModelo> ordenInicial = orden.ToList();
                var azar = new Random(ConstructorInstantaneas.Hash(sesion.Id.ToString("N") + "|" + mostradas.Count));
                for (int i = 0; i < huecos && orden.Count > 0; i++)
                {
                    int posicion = MotorSugerenciasProductos.HuecoLibre(ocupadas);
                    if (posicion == 0)
                    {
                        break;
                    }
                    int? indiceExploracion = explora && posicion == MotorSugerenciasProductos.POSICION_EXPLORACION
                        ? MotorSugerenciasProductos.IndiceExploracion(ordenInicial.Count, azar) : null;
                    CandidatoModelo elegida = indiceExploracion.HasValue && orden.Contains(ordenInicial[indiceExploracion.Value])
                        ? ordenInicial[indiceExploracion.Value] : orden[0];
                    bool exploracion = indiceExploracion.HasValue && ReferenceEquals(elegida, ordenInicial[indiceExploracion.Value]);
                    _ = orden.Remove(elegida);
                    _ = await Registrar(elegida.Candidato, posicion, elegida.Fuentes, exploracion, modelo.Version).ConfigureAwait(false);
                }

                // NestoAPI#682 con el modelo: la mejor nueva frente a la visible sin contestar más floja (P1 de ahora). La del
                // hueco de exploración no se toca (si no, la exploración no duraría ni un cambio de carrito).
                if (permitirSustitucion && orden.Count > 0)
                {
                    var sustituibles = visibles
                        .Where(v => mantenidas.Contains(v.Id) && !(explora && v.Posicion == MotorSugerenciasProductos.POSICION_EXPLORACION)
                            && modelo.P1Visibles.ContainsKey(v.Producto.Trim()))
                        .OrderBy(v => modelo.P1Visibles[v.Producto.Trim()]).ThenByDescending(v => v.Posicion)
                        .ToList();
                    SugerenciaMostrada floja = sustituibles.FirstOrDefault();
                    CandidatoModelo mejor = orden[0];
                    if (floja != null && MotorSugerenciasProductos.MejoraClara(mejor.P1, modelo.P1Visibles[floja.Producto.Trim()]))
                    {
                        await Sustituir(floja, visibles, ocupadas).ConfigureAwait(false);
                        _ = await Registrar(mejor.Candidato, floja.Posicion, mejor.Fuentes, false, modelo.Version).ConfigureAwait(false);
                    }
                }
            }
            else
            {
                List<CandidatoSugerencia> conStock = candidatos.Where(c => vendibles.Contains(c.Producto)).ToList();
                List<CandidatoSugerencia> elegidas = MotorSugerenciasProductos.Elegir(conStock, visibles.Select(v => v.Motivo), excluidos, huecos);
                foreach (CandidatoSugerencia elegida in elegidas)
                {
                    int posicion = MotorSugerenciasProductos.HuecoLibre(ocupadas);
                    if (posicion == 0)
                    {
                        break;
                    }
                    _ = await Registrar(elegida, posicion, fuentes.TryGetValue(elegida.Producto.Trim(), out string f) ? f : elegida.Motivo,
                        false, MotorSugerenciasProductos.VERSION_MOTOR).ConfigureAwait(false);
                }

                // NestoAPI#682 sin el modelo (las puntuaciones de fuentes distintas no se comparan): una «comprado junto» nueva
                // con confianza suficiente entra si no hay ninguna a la vista, en el hueco de la más floja según ORDEN_FUENTES.
                if (permitirSustitucion && !visibles.Any(v => v.Motivo == MotivosSugerencia.COMPRADO_JUNTO))
                {
                    var yaElegidas = new HashSet<string>(elegidas.Select(e => e.Producto), StringComparer.OrdinalIgnoreCase);
                    CandidatoSugerencia junto = conStock
                        .Where(c => c.Motivo == MotivosSugerencia.COMPRADO_JUNTO && c.Puntuacion >= MotorSugerenciasProductos.CONFIANZA_MINIMA_SUSTITUCION_REGLAS
                            && !excluidos.Contains(c.Producto) && !yaElegidas.Contains(c.Producto))
                        .OrderByDescending(c => c.Puntuacion).ThenBy(c => c.Producto, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault();
                    SugerenciaMostrada floja = MotorSugerenciasProductos.MasFlojaSegunReglas(visibles.Where(v => mantenidas.Contains(v.Id)));
                    if (junto != null && floja != null)
                    {
                        await Sustituir(floja, visibles, ocupadas).ConfigureAwait(false);
                        _ = await Registrar(junto, floja.Posicion, fuentes.TryGetValue(junto.Producto.Trim(), out string f) ? f : junto.Motivo,
                            false, MotorSugerenciasProductos.VERSION_MOTOR).ConfigureAwait(false);
                    }
                }
            }

            // Nesto#537: las fotos de las que se ven, de una vez (con caché); el resto de la ficha ya está leído
            IDictionary<string, string> urlsFotos = await UrlsFotos(visibles.Select(v => v.Producto)).ConfigureAwait(false);
            resultado.Visibles = visibles
                .OrderBy(v => v.Posicion)
                .Select(v => Dto(v, fichas.TryGetValue(v.Producto.Trim(), out FichaProductoSugerencia f) ? f : null,
                    urlsFotos.TryGetValue(v.Producto.Trim(), out string url) ? url : null))
                .ToList();
            return resultado;
        }

        /// <summary>
        /// NestoAPI#682: la quita del panel como «Sustituida» (con el script Issue682) o con la marca en el Texto (sin él). No
        /// cuenta como ofrecida ni como rechazo y no vuelve a salir en la sesión (sigue en lo ya enseñado).
        /// </summary>
        private async Task Sustituir(SugerenciaMostrada floja, List<SugerenciaMostrada> visibles, List<int> ocupadas)
        {
            if (await repositorio.AdmiteSustituida().ConfigureAwait(false))
            {
                await repositorio.GuardarSustituida(floja.Id).ConfigureAwait(false);
            }
            else
            {
                await repositorio.GuardarSustituidaProvisional(floja.Id).ConfigureAwait(false);
            }
            _ = visibles.Remove(floja);
            _ = ocupadas.Remove(floja.Posicion);
        }

        /// <summary>
        /// NestoAPI#671 (corte 2c): P1 de todas las candidatas (CompradoJunto, LeToca, Potenciar, Parecidos y lo más comprado del
        /// perfil) y de las visibles sin contestar, con las features calculadas EXACTAMENTE como en el entrenamiento
        /// (<see cref="CalculadoraFeatures"/> del núcleo, con «d» = hoy: solo compras anteriores a hoy y las tablas del job de
        /// anoche). Lecturas añadidas: la popularidad de los candidatos (una consulta) y, si hace falta, las fichas de los
        /// productos del perfil que no se tenían (otra) y el perfil sin las compras de hoy (casi nunca). Null si no hay modelo o
        /// algo falla (a ELMAH una vez): entonces, reglas-1. Nunca lanza.
        /// <para>NestoAPI#683: con un modelo de la versión 2 de las features, además el estado del cliente (Clientes.Estado de
        /// hoy, contacto principal) y la popularidad por estado de los candidatos y sus familias (tabla del job, una consulta por
        /// tipo). Con uno de la versión 1 no se leen (el zip no mira esas columnas y quedan a «NADA» y 0).</para>
        /// </summary>
        private async Task<PuntuacionModelo> PuntuarConModelo(DateTime hoy, string cliente, string empresa, List<CompraDiaReposicion> compras,
            HashSet<string> enCarrito, List<ParCompradoJunto> pares, List<AfinidadCliente> afinidades, Dictionary<string, FichaProductoSugerencia> fichas,
            Tuple<string, string> perfil, Dictionary<string, int> clientesPerfil, List<CandidatoSugerencia> todos, Dictionary<string, string> fuentes,
            List<SugerenciaMostrada> quedan, HashSet<string> yaEnsenados, Func<string, bool> vale)
        {
            string version = predictor?.ModeloDisponible();
            if (version == null)
            {
                return null;
            }
            try
            {
                List<Compra> comprasNucleo = MotorSugerenciasProductos.ACompras(compras).ToList();
                Func<string, FichaProducto> ficha = MotorSugerenciasProductos.Buscador(fichas);

                // El perfil como en el entrenamiento (compras anteriores a d); casi siempre es el de las reglas
                Tuple<string, string> perfilModelo = ReglasSugerencias.Perfil(comprasNucleo.Where(c => c.Dia < hoy), ficha);
                Dictionary<string, int> clientesPerfilModelo = clientesPerfil;
                if (perfilModelo != null && (perfil == null || !string.Equals(perfil.Item1, perfilModelo.Item1, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(perfil.Item2, perfilModelo.Item2, StringComparison.OrdinalIgnoreCase)))
                {
                    clientesPerfilModelo = await repositorio.LeerClientesPerfil(perfilModelo.Item1, perfilModelo.Item2).ConfigureAwait(false)
                        ?? new Dictionary<string, int>();
                }
                List<ProductoDePerfil> perfiles = perfilModelo == null ? new List<ProductoDePerfil>()
                    : clientesPerfilModelo.Where(c => !string.IsNullOrWhiteSpace(c.Key)).Select(c => new ProductoDePerfil
                    {
                        Grupo = perfilModelo.Item1 ?? string.Empty,
                        SubGrupo = perfilModelo.Item2 ?? string.Empty,
                        Producto = c.Key.Trim(),
                        Clientes = c.Value
                    }).ToList();

                // Las fichas de los primeros del perfil (el modelo coge los 15 primeros que se pueden sugerir)
                List<string> sinFicha = perfiles.OrderByDescending(p => p.Clientes).ThenBy(p => p.Producto, StringComparer.OrdinalIgnoreCase)
                    .Take(MotorSugerenciasProductos.FICHAS_PERFIL_MODELO).Select(p => p.Producto)
                    .Where(p => !fichas.ContainsKey(p)).ToList();
                if (sinFicha.Count > 0)
                {
                    Dictionary<string, FichaProductoSugerencia> nuevas = await repositorio.LeerFichas(empresa, sinFicha).ConfigureAwait(false);
                    foreach (KeyValuePair<string, FichaProductoSugerencia> f in nuevas ?? new Dictionary<string, FichaProductoSugerencia>())
                    {
                        fichas[f.Key.Trim()] = f.Value;
                    }
                }

                List<ParComprado> paresNucleo = pares.Where(p => p != null).Select(MotorSugerenciasProductos.AParNucleo).ToList();
                List<AfinidadProducto> afinidadesNucleo = afinidades.Where(a => a?.Producto != null).Select(a => new AfinidadProducto
                {
                    Cliente = (a.Cliente ?? cliente).Trim(),
                    Producto = a.Producto.Trim(),
                    Afinidad = a.Afinidad,
                    Rango = a.Rango
                }).ToList();
                // Las que la API también quiere puntuar: todas sus candidatas (Potenciar no lo propone el modelo) y las visibles
                List<string> adicionales = todos.Select(c => c.Producto).Concat(quedan.Select(m => m.Producto.Trim()))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                int maximoPorFuente = new OpcionesEntrenamiento().MaximoCandidatosPorFuente;

                // Dos pasadas: la primera da los candidatos (la popularidad no cambia cuáles son) y la segunda, con su popularidad
                ContextoInstantanea sinPopularidad = ContextoInstantanea.Desde(hoy, paresNucleo, afinidadesNucleo, null, perfiles);
                ResultadoCandidatos previos = CalculadoraFeatures.Calcular(sinPopularidad, cliente, comprasNucleo, enCarrito, ficha, hoy, maximoPorFuente, adicionales);
                List<PopularidadProducto> popularidad = await repositorio.LeerPopularidad(previos.Filas.Select(f => f.Producto)).ConfigureAwait(false)
                    ?? new List<PopularidadProducto>();
                short? estadoCliente = null;
                List<PopularidadPorEstado> popularidadEstado = null;
                if (predictor.VersionFeatures >= 2)
                {
                    estadoCliente = await repositorio.LeerEstadoCliente(empresa, cliente).ConfigureAwait(false);
                    popularidadEstado = await repositorio.LeerPopularidadEstado(EstadosCliente.Grupo(estadoCliente),
                        previos.Filas.Select(f => f.Producto), previos.Filas.Select(f => f.Familia).Where(f => !string.IsNullOrWhiteSpace(f))).ConfigureAwait(false);
                }
                ContextoInstantanea contexto = ContextoInstantanea.Desde(hoy, paresNucleo, afinidadesNucleo,
                    popularidad.Where(p => p?.Producto != null).Select(p => new PopularidadDeProducto
                    {
                        Producto = p.Producto.Trim(),
                        Clientes12Meses = p.Clientes12Meses,
                        Clientes3Meses = p.Clientes3Meses,
                        Cestas12Meses = p.Cestas12Meses,
                        Rango = p.Rango
                    }), perfiles, popularidadEstado);
                ResultadoCandidatos features = CalculadoraFeatures.Calcular(contexto, cliente, comprasNucleo, enCarrito, ficha, hoy, maximoPorFuente, adicionales,
                    estadoCliente);
                List<ModeloSugerenciaEntrada> entradas = features.Filas.Cast<ModeloSugerenciaEntrada>().ToList();
                IList<float> probabilidades = predictor.Puntuar(entradas);
                if (probabilidades == null || probabilidades.Count != entradas.Count)
                {
                    throw new InvalidOperationException($"El modelo ha devuelto {probabilidades?.Count ?? 0} probabilidades para {entradas.Count} candidatos.");
                }

                var resultado = new PuntuacionModelo { Version = version };
                var visibles = new HashSet<string>(quedan.Select(m => m.Producto.Trim()), StringComparer.OrdinalIgnoreCase);
                Dictionary<string, CandidatoSugerencia> deLaApi = todos
                    .GroupBy(c => c.Producto.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.OrderBy(c => IndiceFuente(c.Motivo)).ThenByDescending(c => c.Puntuacion).First(), StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < features.Filas.Count; i++)
                {
                    FilaEntrenamiento fila = features.Filas[i];
                    string producto = fila.Producto.Trim();
                    float p1 = probabilidades[i];
                    if (visibles.Contains(producto))
                    {
                        resultado.P1Visibles[producto] = p1;
                        continue;
                    }
                    if (yaEnsenados.Contains(producto) || !vale(producto))
                    {
                        continue;
                    }
                    CandidatoSugerencia aEnsenar;
                    if (deLaApi.TryGetValue(producto, out CandidatoSugerencia api))
                    {
                        aEnsenar = api;
                    }
                    else if (fila.EsPerfil > 0)
                    {
                        // Lo más comprado del perfil: solo lo propone el modelo; al vendedor, como «Clientes parecidos»
                        aEnsenar = new CandidatoSugerencia
                        {
                            Producto = producto,
                            Motivo = MotivosSugerencia.PARECIDOS,
                            Texto = MotorSugerenciasProductos.TextoPerfil((int)fila.ClientesPerfil)
                        };
                    }
                    else
                    {
                        continue; // otra fuente con un dato que la API no ve igual (p. ej. compras de hoy): no se enseña
                    }
                    // Las de la API; lo que solo propone el perfil, solo «Perfil» (el Motivo «Parecidos» es para enseñarlo)
                    string fuentesProducto = fuentes.TryGetValue(producto, out string f) ? f : (api != null ? api.Motivo : null);
                    if (fila.EsPerfil > 0)
                    {
                        fuentesProducto = string.IsNullOrEmpty(fuentesProducto) ? ModeloSugerenciasProductos.Reglas.Fuentes.PERFIL
                            : fuentesProducto + "," + ModeloSugerenciasProductos.Reglas.Fuentes.PERFIL;
                    }
                    resultado.Orden.Add(new CandidatoModelo
                    {
                        Candidato = new CandidatoSugerencia
                        {
                            Producto = producto,
                            Motivo = aEnsenar.Motivo,
                            Texto = aEnsenar.Texto,
                            Puntuacion = Math.Round((decimal)p1, 4, MidpointRounding.AwayFromZero)
                        },
                        P1 = p1,
                        Fuentes = fuentesProducto,
                        EsHabitual = fila.EsHabitual > 0,
                        Familia = fila.Familia
                    });
                }
                resultado.Orden = resultado.Orden.OrderByDescending(c => c.P1)
                    .ThenBy(c => c.Candidato.Producto, StringComparer.OrdinalIgnoreCase).ToList();
                return resultado;
            }
            catch (Exception ex)
            {
                predictor.AvisarFallo(ex);
                return null;
            }
        }

        private static int IndiceFuente(string motivo)
        {
            int i = Array.IndexOf(MotorSugerenciasProductos.ORDEN_FUENTES, motivo);
            return i < 0 ? MotorSugerenciasProductos.ORDEN_FUENTES.Length : i;
        }

        internal static SugerenciaProductoDTO Dto(SugerenciaMostrada mostrada, FichaProductoSugerencia ficha, string urlFoto)
        {
            string familia = string.IsNullOrWhiteSpace(ficha?.NombreFamilia) ? ficha?.Familia?.Trim() : ficha.NombreFamilia.Trim();
            return new SugerenciaProductoDTO
            {
                IdSugerencia = mostrada.Id,
                Producto = mostrada.Producto.Trim(),
                Nombre = ficha?.Nombre,
                Motivo = mostrada.Motivo,
                Texto = mostrada.Texto,
                Posicion = mostrada.Posicion,
                UrlFoto = string.IsNullOrWhiteSpace(urlFoto) ? null : urlFoto.Trim(),
                Tamano = ficha?.Tamano,
                UnidadMedida = string.IsNullOrWhiteSpace(ficha?.UnidadMedida) ? null : ficha.UnidadMedida.Trim(),
                Familia = string.IsNullOrEmpty(familia) ? null : familia,
                NombreConTamano = NombreConTamano(ficha?.Nombre, ficha?.Tamano, ficha?.UnidadMedida)
            };
        }

        /// <summary>
        /// Nesto#537: «Crema Reafirmante 500 ml». El mismo formato que el tamaño de los pedidos de compra y de las etiquetas
        /// (GeneradorPdfPedidoCompra.FormatoTamanno): sin tamaño (null o 0), el nombre solo.
        /// </summary>
        internal static string NombreConTamano(string nombre, short? tamano, string unidadMedida)
        {
            string limpio = nombre?.Trim();
            if (string.IsNullOrEmpty(limpio))
            {
                return null;
            }
            if (!tamano.HasValue || tamano.Value == 0)
            {
                return limpio;
            }
            string medida = tamano.Value.ToString(CultureInfo.GetCultureInfo("es-ES"));
            string unidad = unidadMedida?.Trim();
            return string.IsNullOrEmpty(unidad) ? $"{limpio} {medida}" : $"{limpio} {medida} {unidad}";
        }

        /// <summary>Nunca falla: sin servicio de fotos o si la tienda no contesta, las fichas van sin foto.</summary>
        private async Task<IDictionary<string, string>> UrlsFotos(IEnumerable<string> productos)
        {
            var vacio = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            List<string> lista = productos.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToList();
            if (fotos == null || lista.Count == 0)
            {
                return vacio;
            }
            try
            {
                IDictionary<string, string> urls = await fotos.Urls(lista).ConfigureAwait(false);
                return urls == null ? vacio : new Dictionary<string, string>(urls, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                registrarError(new Exception($"[Sugerencias #537] No se han podido leer las fotos de las sugerencias: {ex.Message}", ex));
                return vacio;
            }
        }

        internal static Dictionary<string, int> Carrito(IEnumerable<ProductoCarritoSugerenciaDTO> productos)
        {
            var carrito = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (ProductoCarritoSugerenciaDTO p in (productos ?? Enumerable.Empty<ProductoCarritoSugerenciaDTO>())
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Producto)))
            {
                string producto = p.Producto.Trim();
                carrito[producto] = (carrito.TryGetValue(producto, out int antes) ? antes : 0) + p.Cantidad;
            }
            return carrito;
        }

        /// <summary>Las líneas de producto del pedido guardado: producto → unidades.</summary>
        internal static Dictionary<string, int> LineasPedido(PedidoVentaDTO pedido)
        {
            var lineas = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (LineaPedidoVentaDTO linea in (pedido?.Lineas ?? Enumerable.Empty<LineaPedidoVentaDTO>())
                .Where(l => l != null && !string.IsNullOrWhiteSpace(l.Producto)
                    && (l.tipoLinea == null || l.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO)))
            {
                string producto = linea.Producto.Trim();
                lineas[producto] = (lineas.TryGetValue(producto, out int antes) ? antes : 0) + linea.Cantidad;
            }
            return lineas;
        }

        private static string Aplicacion(string aplicacion)
        {
            string limpia = aplicacion?.Trim();
            if (string.Equals(limpia, AplicacionesSugerencias.NESTO, StringComparison.OrdinalIgnoreCase))
            {
                return AplicacionesSugerencias.NESTO;
            }
            return string.Equals(limpia, AplicacionesSugerencias.NESTO_APP, StringComparison.OrdinalIgnoreCase) ? AplicacionesSugerencias.NESTO_APP : null;
        }

        private static string Respuesta(string respuesta)
        {
            string limpia = respuesta?.Trim();
            if (string.Equals(limpia, RespuestasSugerencia.ANADIDO, StringComparison.OrdinalIgnoreCase)
                || string.Equals(limpia, "Añadido", StringComparison.OrdinalIgnoreCase))
            {
                return RespuestasSugerencia.ANADIDO;
            }
            if (string.Equals(limpia, RespuestasSugerencia.DESCARTADO_VENDEDOR, StringComparison.OrdinalIgnoreCase))
            {
                return RespuestasSugerencia.DESCARTADO_VENDEDOR;
            }
            return string.Equals(limpia, RespuestasSugerencia.NO_LE_INTERESA, StringComparison.OrdinalIgnoreCase) ? RespuestasSugerencia.NO_LE_INTERESA : null;
        }

        private static bool Igual(string a, string b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

        private static string Limpio(string texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
    }
}
