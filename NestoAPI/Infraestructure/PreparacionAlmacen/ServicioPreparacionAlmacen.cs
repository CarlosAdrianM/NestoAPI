using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    public interface IServicioPreparacionAlmacen
    {
        /// <summary>Hay almacenamiento para las fotos de los bultos (cadena de conexión puesta).</summary>
        bool FotosConfiguradas { get; }
        Task<PickingAlmacenDTO> LeerPicking(string empresa, int picking);
        Task<List<PickingEnCursoDTO>> LeerPickingsEnCurso(string empresa, string almacen);
        /// <summary>Cómo va el picking por ola. Null si el picking no tiene líneas.</summary>
        Task<EstadoPickingDTO> LeerEstadoPicking(string empresa, int picking);
        /// <summary>NestoAPI#574: lo que hay por recoger en un almacén, sea un picking o una reposición.</summary>
        Task<List<RecogidaPendienteDTO>> LeerRecogidasPendientes(string empresa, string almacen);
        /// <summary>El recorrido de una recogida con cómo va. Null si no existe (o es de un tipo que todavía no se ofrece).</summary>
        Task<RecogidaAlmacenDTO> LeerRecogida(string empresa, string tipo, int numero);
        Task<PackingAlmacenDTO> LeerPacking(string empresa, int picking);
        /// <summary>El packing de un solo pedido, con su picking en curso. Null si el pedido no tiene picking.</summary>
        Task<PackingAlmacenDTO> LeerPackingDePedido(string empresa, int pedido);
        Task<ResultadoEscaneosAlmacenDTO> GuardarEscaneos(string empresa, IEnumerable<EscaneoAlmacenDTO> escaneos, string usuario);
        Task<BultoAlmacenDTO> GuardarFotoBulto(FotoBultoAlmacen foto, string usuario);
        Task<List<BultoAlmacenDTO>> LeerBultos(string empresa, int pedido);
        /// <summary>Enlace temporal a la foto de un bulto. Null si el bulto no existe o no tiene foto.</summary>
        Task<Uri> EnlaceFotoBulto(int idBulto);
        /// <summary>
        /// Lo mismo, pero para quien llega con el enlace público (sin usuario). Null si el enlace no
        /// es bueno, el bulto no existe o no tiene foto: desde fuera no se distingue un caso de otro.
        /// </summary>
        Task<Uri> EnlaceFotoBultoPublico(string token);
        /// <summary>Lo pedido frente a lo metido en las cajas, y los bultos con su foto. Null si el pedido no tiene picking.</summary>
        Task<EstadoPreparacionPedidoDTO> LeerEstadoPedido(string empresa, int pedido, int? picking);
    }

    /// <summary>La foto de un bulto tal como llega del móvil.</summary>
    public class FotoBultoAlmacen
    {
        public Guid IdCliente { get; set; }
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        /// <summary>
        /// Otros pedidos del mismo cliente que van en la misma caja (dos pedidos para tener dos
        /// facturas, pero un solo bulto). La foto se sube una vez y cada pedido tiene su fila.
        /// </summary>
        public List<int> OtrosPedidos { get; set; }
        public int Picking { get; set; }
        public int Bulto { get; set; }
        public byte[] Imagen { get; set; }
        public DateTime? FechaFoto { get; set; }
        public string Dispositivo { get; set; }
    }

    /// <summary>
    /// NestoAPI#556: las reglas de la preparación de pedidos con Ariadna, la app de almacén. No toca
    /// nada de lo que hay en uso: lee el picking que ya saca Nesto y guarda la evidencia (escaneos y
    /// fotos de bultos) en sus dos tablas.
    /// </summary>
    public class ServicioPreparacionAlmacen : IServicioPreparacionAlmacen, IDisposable
    {
        /// <summary>Una foto de bulto ronda los 300 KB. Esto es un tope contra subidas absurdas.</summary>
        public const int TAMANO_MAXIMO_FOTO = 5 * 1024 * 1024;
        /// <summary>Lo que dura el enlace para ver una foto (NestoAPI#556).</summary>
        public static readonly TimeSpan VIGENCIA_ENLACE_FOTO = TimeSpan.FromMinutes(15);
        /// <summary>Un lote de la cola del móvil. Más que esto es que algo va mal en la app.</summary>
        public const int MAXIMO_ESCANEOS_POR_LOTE = 500;

        private readonly IRepositorioPreparacionAlmacen repositorio;
        private readonly IAlmacenFotosBultos fotos;
        private readonly NVEntities dbPropio;
        private readonly string claveEnlacesFotos;

        public ServicioPreparacionAlmacen()
        {
            dbPropio = new NVEntities();
            repositorio = new RepositorioPreparacionAlmacen(dbPropio);
            fotos = new AlmacenFotosBultosAzure();
            claveEnlacesFotos = System.Configuration.ConfigurationManager.AppSettings[EnlacePublicoFotoBulto.CLAVE_CONFIGURACION];
        }

        internal ServicioPreparacionAlmacen(IRepositorioPreparacionAlmacen repositorio, IAlmacenFotosBultos fotos, string claveEnlacesFotos = null)
        {
            this.repositorio = repositorio;
            this.fotos = fotos;
            this.claveEnlacesFotos = claveEnlacesFotos;
        }

        /// <summary>Pone a cada bulto con foto su enlace público (si hay clave para firmarlo).</summary>
        private BultoAlmacenDTO ConEnlacePublico(BultoAlmacenDTO bulto)
        {
            if (bulto != null)
            {
                bulto.RutaFotoPublica = string.IsNullOrWhiteSpace(bulto.RutaBlob) || !fotos.Configurado
                    ? null
                    : EnlacePublicoFotoBulto.Ruta(claveEnlacesFotos, bulto.Id, bulto.IdCliente);
            }
            return bulto;
        }

        private List<BultoAlmacenDTO> ConEnlacePublico(List<BultoAlmacenDTO> bultos)
        {
            bultos?.ForEach(b => ConEnlacePublico(b));
            return bultos;
        }

        public bool FotosConfiguradas => fotos.Configurado;

        public async Task<PickingAlmacenDTO> LeerPicking(string empresa, int picking)
        {
            List<LineaPickingAlmacenDTO> lineas = await repositorio.LeerLineasPicking(empresa, picking).ConfigureAwait(false);
            return new PickingAlmacenDTO
            {
                Empresa = empresa,
                Picking = picking,
                Lineas = CasadorEscaneos.OrdenarRecorrido(lineas)
            };
        }

        public Task<List<PickingEnCursoDTO>> LeerPickingsEnCurso(string empresa, string almacen)
        {
            return repositorio.LeerPickingsEnCurso(empresa, almacen);
        }

        public async Task<EstadoPickingDTO> LeerEstadoPicking(string empresa, int picking)
        {
            List<LineaPickingAlmacenDTO> lineas = await repositorio.LeerLineasPicking(empresa, picking).ConfigureAwait(false);
            if (!lineas.Any())
            {
                return null;
            }
            List<LecturaPickingAlmacen> lecturas = await repositorio.LeerLecturasDelPicking(empresa, picking).ConfigureAwait(false);
            return MontarEstadoPicking(empresa, picking, lineas, lecturas);
        }

        public async Task<List<RecogidaPendienteDTO>> LeerRecogidasPendientes(string empresa, string almacen)
        {
            // De momento solo los pickings: la reposición a tienda no tiene documento propio hasta
            // que se genera (#553). Cuando lo tenga, se suma aquí y la app no cambia.
            return (await repositorio.LeerPickingsEnCurso(empresa, almacen).ConfigureAwait(false))
                .Select(p => new RecogidaPendienteDTO
                {
                    Tipo = CasadorEscaneos.ORIGEN_PICKING,
                    Numero = p.Picking,
                    Destino = CasadorEscaneos.DESTINO_PICKING,
                    Lineas = p.Lineas,
                    Pedidos = p.Pedidos,
                    Unidades = p.Unidades
                })
                .ToList();
        }

        public async Task<RecogidaAlmacenDTO> LeerRecogida(string empresa, string tipo, int numero)
        {
            if (CasadorEscaneos.NormalizarTipoOrigen(tipo) != CasadorEscaneos.ORIGEN_PICKING)
            {
                return null;
            }
            List<LineaPickingAlmacenDTO> recorrido = CasadorEscaneos.OrdenarRecorrido(
                await repositorio.LeerLineasPicking(empresa, numero).ConfigureAwait(false));
            if (recorrido.Count == 0)
            {
                return null;
            }
            List<LecturaPickingAlmacen> lecturas = await repositorio.LeerLecturasDelPicking(empresa, numero).ConfigureAwait(false);
            return MontarRecogida(empresa, CasadorEscaneos.ORIGEN_PICKING, numero, CasadorEscaneos.DESTINO_PICKING, recorrido, lecturas);
        }

        internal static RecogidaAlmacenDTO MontarRecogida(string empresa, string tipo, int numero, string destino,
            List<LineaPickingAlmacenDTO> recorrido, List<LecturaPickingAlmacen> lecturas)
        {
            EstadoPickingDTO estado = MontarEstadoPicking(empresa, numero, recorrido, lecturas);
            List<LineaRecogidaDTO> lineas = CasadorEscaneos.RepartirLoResuelto(recorrido,
                (lecturas ?? new List<LecturaPickingAlmacen>())
                    .Select(l => new CasadorEscaneos.Cantidad { Producto = l.Producto, Unidades = l.Unidades + l.Faltas }));
            return new RecogidaAlmacenDTO
            {
                Empresa = empresa,
                Tipo = tipo,
                Numero = numero,
                Destino = destino,
                Lineas = lineas,
                SiguienteOrden = lineas.Where(l => l.Pendiente > 0).Select(l => (int?)l.Orden).FirstOrDefault(),
                Terminada = estado.Terminado,
                Completa = estado.Completo
            };
        }

        /// <summary>
        /// Un producto puede estar en varios huecos: aquí se mira por producto. «Terminado» es que
        /// no queda nada por resolver (cogido o dado por falta); «completo», que además no hay
        /// faltas ni nada de más. Así dos mozos pueden repartirse un picking o retomarlo.
        /// </summary>
        internal static EstadoPickingDTO MontarEstadoPicking(string empresa, int picking,
            IEnumerable<LineaPickingAlmacenDTO> lineas, IEnumerable<LecturaPickingAlmacen> lecturas)
        {
            List<LecturaPickingAlmacen> leido = (lecturas ?? Enumerable.Empty<LecturaPickingAlmacen>()).ToList();
            List<DiferenciaPreparacionDTO> diferencias = CasadorEscaneos.Casar(
                (lineas ?? Enumerable.Empty<LineaPickingAlmacenDTO>())
                    .Select(l => new CasadorEscaneos.Cantidad { Producto = l.Producto, Descripcion = l.Descripcion, Unidades = l.Cantidad }),
                leido.Select(l => new CasadorEscaneos.Cantidad { Producto = l.Producto, Unidades = l.Unidades }));

            Dictionary<string, int> faltas = leido
                .GroupBy(l => l.Producto?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Sum(l => l.Faltas), StringComparer.OrdinalIgnoreCase);
            foreach (DiferenciaPreparacionDTO diferencia in diferencias)
            {
                diferencia.Faltas = faltas.TryGetValue(diferencia.Producto, out int unidades) ? unidades : 0;
            }

            return new EstadoPickingDTO
            {
                Empresa = empresa,
                Picking = picking,
                Productos = diferencias,
                Terminado = diferencias.Any() && diferencias.All(d => !d.Ajeno && d.Leido + d.Faltas >= d.Esperado),
                Completo = CasadorEscaneos.EstaCompleto(diferencias) && diferencias.All(d => d.Faltas == 0)
            };
        }

        public async Task<PackingAlmacenDTO> LeerPacking(string empresa, int picking)
        {
            List<FilaPackingAlmacen> filas = await repositorio.LeerLineasPacking(empresa, picking, null).ConfigureAwait(false);
            return MontarPacking(empresa, picking, filas);
        }

        public async Task<PackingAlmacenDTO> LeerPackingDePedido(string empresa, int pedido)
        {
            int? picking = await repositorio.PickingEnCursoDelPedido(empresa, pedido).ConfigureAwait(false);
            if (!picking.HasValue)
            {
                return null;
            }
            List<FilaPackingAlmacen> filas = await repositorio.LeerLineasPacking(empresa, picking.Value, pedido).ConfigureAwait(false);
            return MontarPacking(empresa, picking.Value, filas);
        }

        /// <summary>
        /// Agrupa las líneas como el packing list de hoy: por cliente y dirección de entrega, y dentro
        /// por pedido. Un código es «duplicado» si lo comparten dos productos de la misma entrega.
        /// </summary>
        internal static PackingAlmacenDTO MontarPacking(string empresa, int picking, IEnumerable<FilaPackingAlmacen> filas)
        {
            var packing = new PackingAlmacenDTO { Empresa = empresa, Picking = picking };

            foreach (var entrega in (filas ?? Enumerable.Empty<FilaPackingAlmacen>())
                .GroupBy(f => new { Cliente = f.Cliente?.Trim(), Contacto = f.Contacto?.Trim() }))
            {
                FilaPackingAlmacen primera = entrega.First();
                HashSet<string> duplicados = CasadorEscaneos.CodigosDuplicados(
                    entrega.Select(f => new KeyValuePair<string, string>(f.Producto, f.CodigoBarras)));

                packing.Entregas.Add(new EntregaPackingAlmacenDTO
                {
                    Cliente = entrega.Key.Cliente,
                    Contacto = entrega.Key.Contacto,
                    Nombre = primera.Nombre?.Trim(),
                    Direccion = primera.Direccion?.Trim(),
                    CodigoPostal = primera.CodigoPostal?.Trim(),
                    Poblacion = primera.Poblacion?.Trim(),
                    Ruta = primera.Ruta?.Trim(),
                    Pedidos = entrega.GroupBy(f => f.Pedido).OrderBy(g => g.Key).Select(g => new PedidoPackingAlmacenDTO
                    {
                        Pedido = g.Key,
                        ComentarioPicking = string.IsNullOrWhiteSpace(g.First().ComentarioPicking) ? null : g.First().ComentarioPicking.Trim(),
                        Lineas = g.OrderBy(f => f.LineaPedido).Select(f =>
                        {
                            string codigo = string.IsNullOrWhiteSpace(f.CodigoBarras) ? null : f.CodigoBarras.Trim();
                            return new LineaPackingAlmacenDTO
                            {
                                LineaPedido = f.LineaPedido,
                                Producto = f.Producto?.Trim(),
                                Descripcion = f.Descripcion?.Trim(),
                                CodigoBarras = codigo,
                                SinCodigo = codigo == null,
                                CodigoDuplicado = codigo != null && duplicados.Contains(codigo),
                                Cantidad = f.Cantidad
                            };
                        }).ToList()
                    }).ToList()
                });
            }
            return packing;
        }

        public async Task<ResultadoEscaneosAlmacenDTO> GuardarEscaneos(string empresa, IEnumerable<EscaneoAlmacenDTO> escaneos, string usuario)
        {
            List<EscaneoAlmacenDTO> lote = (escaneos ?? Enumerable.Empty<EscaneoAlmacenDTO>()).ToList();
            if (lote.Count > MAXIMO_ESCANEOS_POR_LOTE)
            {
                throw new NestoBusinessException(
                    $"No se pueden mandar más de {MAXIMO_ESCANEOS_POR_LOTE} escaneos de una vez (han llegado {lote.Count}).");
            }

            var resultado = new ResultadoEscaneosAlmacenDTO();
            var vistos = new HashSet<Guid>();
            foreach (EscaneoAlmacenDTO escaneo in lote)
            {
                // Uno malo no tira el lote: el móvil vacía su cola y reintenta solo los rechazados
                string motivo = CasadorEscaneos.MotivoDeRechazo(escaneo);
                if (motivo != null)
                {
                    resultado.Rechazados.Add(new EscaneoRechazadoDTO { IdCliente = escaneo?.IdCliente ?? Guid.Empty, Motivo = motivo });
                    continue;
                }
                if (!vistos.Add(escaneo.IdCliente))
                {
                    resultado.Repetidos++;
                    continue;
                }
                if (await repositorio.InsertarEscaneo(empresa, escaneo, UsuarioAuditoriaHelper.ParaAuditoria(usuario)).ConfigureAwait(false))
                {
                    resultado.Guardados++;
                }
                else
                {
                    resultado.Repetidos++;
                }
            }
            return resultado;
        }

        public async Task<BultoAlmacenDTO> GuardarFotoBulto(FotoBultoAlmacen foto, string usuario)
        {
            string motivo = MotivoDeRechazoDeLaFoto(foto);
            if (motivo != null)
            {
                throw new NestoBusinessException(motivo);
            }

            List<int> otrosPedidos = (foto.OtrosPedidos ?? new List<int>())
                .Where(p => p > 0 && p != foto.Pedido).Distinct().ToList();
            string hash = HashSha256(foto.Imagen);
            string usuarioAuditoria = UsuarioAuditoriaHelper.ParaAuditoria(usuario);

            // Reenvío de la cola del móvil: la foto ya está guardada, no se sube otra vez
            BultoAlmacenDTO principal = await repositorio.LeerBultoPorIdCliente(foto.IdCliente).ConfigureAwait(false);
            if (principal == null)
            {
                // Todos los pedidos de la caja se comprueban ANTES de subir nada
                foreach (int pedido in new[] { foto.Pedido }.Concat(otrosPedidos))
                {
                    if (!await repositorio.ExistePedidoEnPicking(foto.Empresa, pedido, foto.Picking).ConfigureAwait(false))
                    {
                        throw new NestoBusinessException($"El pedido {pedido} no está en el picking {foto.Picking}.");
                    }
                }
                if (!fotos.Configurado)
                {
                    throw new NestoBusinessException("El almacenamiento de las fotos de los bultos no está configurado.")
                    {
                        StatusCode = System.Net.HttpStatusCode.ServiceUnavailable
                    };
                }

                DateTime fechaFoto = foto.FechaFoto ?? DateTime.Now;
                string ruta = RutaDeLaFoto(foto.Empresa, foto.Pedido, foto.Picking, foto.Bulto, fechaFoto);

                // Primero la foto y después la fila: una fila sin foto diría que hay evidencia que no existe
                await fotos.Subir(ruta, foto.Imagen, "image/jpeg").ConfigureAwait(false);

                principal = await repositorio.GuardarBulto(new BultoAlmacenDTO
                {
                    IdCliente = foto.IdCliente,
                    Empresa = foto.Empresa,
                    Pedido = foto.Pedido,
                    Picking = foto.Picking,
                    Bulto = foto.Bulto,
                    RutaBlob = ruta,
                    Usuario = usuarioAuditoria,
                    FechaFoto = fechaFoto
                }, hash, foto.Imagen.Length, foto.Dispositivo).ConfigureAwait(false);
            }

            // Los demás pedidos de la caja: la misma foto, cada uno con su fila. También en un reenvío,
            // por si la primera vez se quedó a medias.
            foreach (int pedido in otrosPedidos)
            {
                Guid idDerivado = IdParaOtroPedido(foto.IdCliente, pedido);
                if (await repositorio.LeerBultoPorIdCliente(idDerivado).ConfigureAwait(false) != null)
                {
                    continue;
                }
                _ = await repositorio.GuardarBulto(new BultoAlmacenDTO
                {
                    IdCliente = idDerivado,
                    Empresa = foto.Empresa,
                    Pedido = pedido,
                    Picking = foto.Picking,
                    Bulto = foto.Bulto,
                    RutaBlob = principal?.RutaBlob,
                    Usuario = usuarioAuditoria,
                    FechaFoto = principal?.FechaFoto
                }, hash, foto.Imagen.Length, foto.Dispositivo).ConfigureAwait(false);
            }

            return ConEnlacePublico(principal);
        }

        /// <summary>
        /// El identificador de la fila de otro pedido que comparte la caja: siempre el mismo para la
        /// misma foto y el mismo pedido, para que un reenvío tampoco la duplique.
        /// </summary>
        internal static Guid IdParaOtroPedido(Guid idCliente, int pedido)
        {
            using (MD5 md5 = MD5.Create())
            {
                return new Guid(md5.ComputeHash(idCliente.ToByteArray().Concat(BitConverter.GetBytes(pedido)).ToArray()));
            }
        }

        internal static string MotivoDeRechazoDeLaFoto(FotoBultoAlmacen foto)
        {
            if (foto == null)
            {
                return "No ha llegado ninguna foto.";
            }
            if (foto.IdCliente == Guid.Empty)
            {
                return "Falta el identificador de la foto (IdCliente).";
            }
            if (foto.Pedido <= 0 || foto.Picking <= 0)
            {
                return "Faltan el pedido o el picking del bulto.";
            }
            if (foto.Bulto <= 0 || foto.Bulto > short.MaxValue)
            {
                return "El número de bulto no es válido.";
            }
            if (foto.Imagen == null || foto.Imagen.Length == 0)
            {
                return "La foto llega vacía.";
            }
            if (foto.Imagen.Length > TAMANO_MAXIMO_FOTO)
            {
                return $"La foto pesa {foto.Imagen.Length / 1024} KB y el máximo son {TAMANO_MAXIMO_FOTO / 1024} KB.";
            }
            // Los tres primeros bytes de cualquier JPEG
            if (foto.Imagen.Length < 3 || foto.Imagen[0] != 0xFF || foto.Imagen[1] != 0xD8 || foto.Imagen[2] != 0xFF)
            {
                return "La foto tiene que ser un JPEG.";
            }
            return null;
        }

        /// <summary>
        /// Dentro del contenedor: {empresa}/{pedido}/{picking}/bulto-{n}-{fecha}.jpg. La fecha va en
        /// el nombre para que repetir la foto no pise la anterior: la fila apunta a la última, pero
        /// la primera sigue en el almacenamiento.
        /// </summary>
        internal static string RutaDeLaFoto(string empresa, int pedido, int picking, int bulto, DateTime fechaFoto)
        {
            return $"{empresa?.Trim()}/{pedido}/{picking}/bulto-{bulto}-{fechaFoto:yyyyMMddHHmmss}.jpg";
        }

        internal static string HashSha256(byte[] contenido)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(contenido)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        public async Task<List<BultoAlmacenDTO>> LeerBultos(string empresa, int pedido)
        {
            return ConEnlacePublico(await repositorio.LeerBultos(empresa, pedido).ConfigureAwait(false));
        }

        public async Task<Uri> EnlaceFotoBultoPublico(string token)
        {
            if (!EnlacePublicoFotoBulto.ClaveValida(claveEnlacesFotos) || !EnlacePublicoFotoBulto.TryLeerId(token, out int idBulto))
            {
                return null;
            }
            BultoAlmacenDTO bulto = await repositorio.LeerBulto(idBulto).ConfigureAwait(false);
            if (bulto == null || string.IsNullOrWhiteSpace(bulto.RutaBlob) || !fotos.Configurado
                || !EnlacePublicoFotoBulto.EsValido(claveEnlacesFotos, token, bulto.Id, bulto.IdCliente))
            {
                return null;
            }
            return fotos.EnlaceDeLectura(bulto.RutaBlob.Trim(), VIGENCIA_ENLACE_FOTO);
        }

        public async Task<Uri> EnlaceFotoBulto(int idBulto)
        {
            BultoAlmacenDTO bulto = await repositorio.LeerBulto(idBulto).ConfigureAwait(false);
            if (bulto == null || string.IsNullOrWhiteSpace(bulto.RutaBlob) || !fotos.Configurado)
            {
                return null;
            }
            return fotos.EnlaceDeLectura(bulto.RutaBlob.Trim(), VIGENCIA_ENLACE_FOTO);
        }

        public async Task<EstadoPreparacionPedidoDTO> LeerEstadoPedido(string empresa, int pedido, int? picking)
        {
            int? pickingDelPedido = picking ?? await repositorio.PickingEnCursoDelPedido(empresa, pedido).ConfigureAwait(false);
            if (!pickingDelPedido.HasValue)
            {
                return null;
            }

            List<FilaPackingAlmacen> lineas = await repositorio.LeerLineasPacking(empresa, pickingDelPedido.Value, pedido).ConfigureAwait(false);
            List<LecturaProductoAlmacen> lecturas = await repositorio
                .LeerLecturas(empresa, pedido, pickingDelPedido.Value, CasadorEscaneos.FASE_PACKING).ConfigureAwait(false);
            List<BultoAlmacenDTO> bultos = ConEnlacePublico((await repositorio.LeerBultos(empresa, pedido).ConfigureAwait(false))
                .Where(b => b.Picking == pickingDelPedido.Value).ToList());

            List<DiferenciaPreparacionDTO> diferencias = CasadorEscaneos.Casar(
                lineas.Select(l => new CasadorEscaneos.Cantidad { Producto = l.Producto, Descripcion = l.Descripcion, Unidades = l.Cantidad }),
                lecturas.Select(l => new CasadorEscaneos.Cantidad { Producto = l.Producto, Unidades = l.Unidades }));

            return new EstadoPreparacionPedidoDTO
            {
                Empresa = empresa,
                Pedido = pedido,
                Picking = pickingDelPedido.Value,
                Productos = diferencias,
                Completo = CasadorEscaneos.EstaCompleto(diferencias),
                Bultos = bultos,
                TodosLosBultosConFoto = bultos.Any() && bultos.All(b => b.TieneFoto)
            };
        }

        public void Dispose()
        {
            dbPropio?.Dispose();
        }
    }
}
