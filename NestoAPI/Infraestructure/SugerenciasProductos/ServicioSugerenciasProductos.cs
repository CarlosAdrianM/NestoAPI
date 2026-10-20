using NestoAPI.Infraestructure.CorreosPostCompra;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

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
    /// </summary>
    public class ServicioSugerenciasProductos : IServicioSugerenciasProductos
    {
        private readonly IRepositorioSugerenciasProductos repositorio;
        private readonly Func<DateTime> ahora;
        private readonly Action<Exception> registrarError;
        private readonly IFotosProductoAlmacen fotos;

        /// <param name="fotos">Nesto#537: las fotos de la tienda (las de Ariadna, con caché). Null: las fichas van sin foto.</param>
        public ServicioSugerenciasProductos(IRepositorioSugerenciasProductos repositorio, Func<DateTime> ahora = null,
            Action<Exception> registrarError = null, IFotosProductoAlmacen fotos = null)
        {
            this.repositorio = repositorio;
            this.ahora = ahora ?? (() => DateTime.Now);
            this.registrarError = registrarError ?? ElmahHelper.Log;
            this.fotos = fotos;
        }

        /// <summary>
        /// Lo que llaman PostPedidoVenta y PutPedidoVenta después de guardar. Sin sesión de sugerencias no hace nada (ni abre
        /// conexión); con ella, nunca rompe el guardado: los errores van a ELMAH.
        /// </summary>
        public static async Task EnlazarPedidoSinFallar(PedidoVentaDTO pedido)
        {
            if (pedido?.IdSesionSugerencias == null || pedido.IdSesionSugerencias.Value == Guid.Empty)
            {
                return;
            }
            try
            {
                using (NVEntities db = new NVEntities())
                {
                    await new ServicioSugerenciasProductos(new RepositorioSugerenciasProductosSql(db)).EnlazarPedido(pedido).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Sugerencias #670] No se ha podido enlazar la sesión {pedido.IdSesionSugerencias} con el pedido {pedido.numero}: {ex.Message}", ex));
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
            resultado.Sugerencias = (await Rellenar(sesion, carrito, mostradas).ConfigureAwait(false)).Visibles;
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
            ResultadoRelleno relleno = await Rellenar(sesion, carrito, mostradas).ConfigureAwait(false);
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

        /// <summary>Las que se ven: las que se mantienen y las nuevas que llenan los huecos (y se registran).</summary>
        private async Task<ResultadoRelleno> Rellenar(SesionSugerencias sesion, Dictionary<string, int> carrito, List<SugerenciaMostrada> mostradas)
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
            HashSet<string> rechazados = await repositorio.LeerRechazados(empresa, cliente,
                hoy.AddDays(-MotorSugerenciasProductos.DIAS_SIN_REPETIR_RECHAZO)).ConfigureAwait(false)
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SugerenciaMostrada rechazada in mostradas.Where(m => RespuestasSugerencia.ExcluyenSesentaDias(m.Respuesta)))
            {
                _ = rechazados.Add(rechazada.Producto.Trim());
            }
            List<ParCompradoJunto> pares = enCarrito.Count == 0 ? new List<ParCompradoJunto>()
                : await repositorio.LeerPares(enCarrito).ConfigureAwait(false) ?? new List<ParCompradoJunto>();
            List<CandidatoPotenciar> potenciar = await repositorio.LeerPotenciar(empresa, hoy).ConfigureAwait(false) ?? new List<CandidatoPotenciar>();
            List<AfinidadCliente> afinidades = await repositorio.LeerAfinidad(cliente).ConfigureAwait(false) ?? new List<AfinidadCliente>();

            var comprados = new HashSet<string>(compras.Where(c => c.Producto != null).Select(c => c.Producto.Trim()), StringComparer.OrdinalIgnoreCase);
            List<SugerenciaMostrada> sinResponder = mostradas.Where(m => m.Respuesta == null).OrderBy(m => m.Posicion).ThenBy(m => m.Id).ToList();
            IEnumerable<string> productosConFicha = comprados
                .Concat(enCarrito)
                .Concat(pares.Select(p => p.ProductoA)).Concat(pares.Select(p => p.ProductoB))
                .Concat(potenciar.Select(p => p.Producto))
                .Concat(afinidades.Select(a => a.Producto))
                .Concat(sinResponder.Select(m => m.Producto));
            Dictionary<string, FichaProductoSugerencia> fichas = await repositorio.LeerFichas(empresa, productosConFicha).ConfigureAwait(false)
                ?? new Dictionary<string, FichaProductoSugerencia>(StringComparer.OrdinalIgnoreCase);
            fichas = new Dictionary<string, FichaProductoSugerencia>(fichas, StringComparer.OrdinalIgnoreCase);

            Tuple<string, string> perfil = MotorSugerenciasProductos.Perfil(compras, fichas);
            Dictionary<string, int> clientesPerfil = perfil == null ? new Dictionary<string, int>()
                : await repositorio.LeerClientesPerfil(perfil.Item1, perfil.Item2).ConfigureAwait(false) ?? new Dictionary<string, int>();
            clientesPerfil = new Dictionary<string, int>(clientesPerfil, StringComparer.OrdinalIgnoreCase);

            bool Vale(string producto) => MotorSugerenciasProductos.MotivoDescarte(producto,
                fichas.TryGetValue(producto?.Trim() ?? string.Empty, out FichaProductoSugerencia f) ? f : null,
                filtro, enCarrito, pendientes, rechazados) == null;

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

            // El stock (o la fecha de llegada), de una vez, solo para lo que puede salir
            HashSet<string> vendibles = await repositorio.LeerVendibles(empresa,
                quedan.Select(m => m.Producto).Concat(candidatos.Select(c => c.Producto))).ConfigureAwait(false)
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            vendibles = new HashSet<string>(vendibles, StringComparer.OrdinalIgnoreCase);

            List<SugerenciaMostrada> visibles = quedan
                .Where(m => vendibles.Contains(m.Producto.Trim()))
                .Take(MotorSugerenciasProductos.MAXIMO_SUGERENCIAS)
                .ToList();
            int huecos = Math.Max(0, MotorSugerenciasProductos.SUGERENCIAS_VISIBLES - visibles.Count);
            var excluidos = new HashSet<string>(yaEnsenados.Concat(enCarrito), StringComparer.OrdinalIgnoreCase);
            List<CandidatoSugerencia> elegidas = MotorSugerenciasProductos.Elegir(
                candidatos.Where(c => vendibles.Contains(c.Producto)), visibles.Select(v => v.Motivo), excluidos, huecos);

            var resultado = new ResultadoRelleno();
            var ocupadas = visibles.Select(v => (int)v.Posicion).ToList();
            foreach (CandidatoSugerencia elegida in elegidas)
            {
                int posicion = MotorSugerenciasProductos.HuecoLibre(ocupadas);
                if (posicion == 0)
                {
                    break;
                }
                var nueva = new SugerenciaMostrada
                {
                    SesionId = sesion.Id,
                    Producto = elegida.Producto,
                    Motivo = elegida.Motivo,
                    Puntuacion = elegida.Puntuacion,
                    Posicion = (byte)posicion,
                    VersionMotor = MotorSugerenciasProductos.VERSION_MOTOR,
                    Texto = elegida.Texto,
                    FechaMostrada = ahora(),
                    Fuentes = fuentes.TryGetValue(elegida.Producto.Trim(), out string fuentesProducto) ? fuentesProducto : elegida.Motivo,
                    TamanoCarrito = enCarrito.Count,
                    Exploracion = false
                };
                nueva.Id = await repositorio.InsertarMostrada(nueva).ConfigureAwait(false);
                ocupadas.Add(posicion);
                visibles.Add(nueva);
                _ = resultado.Nuevas.Add(nueva.Id);
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
