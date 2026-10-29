using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>
    /// Ariadna#63 (sugerencias 872 y 873): histórico de pickings y packings (api/Almacen/Historico). Permiso por el parámetro
    /// VerHistoricoAlmacen de la propia fila (sin «(defecto)») o Dirección/Informática; máx. 31 días; lo leído por producto del
    /// picking por ola se reparte entre sus líneas.
    /// </summary>
    [TestClass]
    public class HistoricoAlmacenTests
    {
        private const string EMPRESA = "1";
        private static readonly DateTime DIA = new DateTime(2026, 10, 29);

        private IRepositorioHistoricoAlmacen repositorio;
        private List<BultoAlmacenDTO> bultos;
        private ServicioHistoricoAlmacen servicio;

        [TestInitialize]
        public void Setup()
        {
            repositorio = A.Fake<IRepositorioHistoricoAlmacen>();
            bultos = new List<BultoAlmacenDTO>();
            servicio = new ServicioHistoricoAlmacen(repositorio, _ => Task.FromResult(bultos));
        }

        private static FilaLineaHistorico Linea(int picking, int pedido, int linea, string producto, int cantidad)
            => new FilaLineaHistorico { Picking = picking, Pedido = pedido, LineaPedido = linea, Cliente = "15191", NombreCliente = "PELUQUERÍA", Producto = producto, Nombre = "Producto " + producto, Cantidad = cantidad };

        private static FilaLecturaHistorico Lectura(int picking, string producto, string usuario, int leidas, int faltas = 0, int minuto = 0)
            => new FilaLecturaHistorico { Picking = picking, Producto = producto, Usuario = usuario, Leidas = leidas, Faltas = faltas, Primera = DIA.AddHours(8).AddMinutes(minuto), Ultima = DIA.AddHours(9).AddMinutes(minuto) };

        // ================================================================ reparto de lo leído

        [TestMethod]
        public void Repartir_PickingPorOla_LoLeidoVaALasLineasEnOrdenDePedidoYLasFaltasALoQueQueda()
        {
            var lineas = new List<FilaLineaHistorico>
            {
                Linea(99757, 929300, 2, "17404", 3),
                Linea(99757, 929262, 1, "17404", 2),
                Linea(99757, 929262, 5, "22624", 1)
            };
            var lecturas = new List<FilaLecturaHistorico>
            {
                Lectura(99757, "17404", @"NUEVAVISION\Andre", 3, faltas: 1),
                Lectura(99757, "17404", "Pedro", 1, minuto: 5)
            };

            List<LineaPickingHistoricoDTO> resultado = ServicioHistoricoAlmacen.Repartir(lineas, lecturas);

            Assert.AreEqual(3, resultado.Count);
            LineaPickingHistoricoDTO primera = resultado[0];
            Assert.AreEqual(929262, primera.Pedido, "Primero el pedido más bajo");
            Assert.AreEqual(2, primera.Recogida);
            Assert.IsFalse(primera.Falta);
            Assert.AreEqual("Andre", primera.Usuario, "Quien más ha leído, sin dominio");
            Assert.AreEqual(DIA.AddHours(9).AddMinutes(5), primera.FechaHora, "La última lectura del producto");
            Assert.AreEqual("22624", resultado[1].Producto);
            Assert.AreEqual(0, resultado[1].Recogida, "Nadie lo ha leído");
            Assert.IsNull(resultado[1].Usuario);
            Assert.IsNull(resultado[1].FechaHora);
            LineaPickingHistoricoDTO otra = resultado[2];
            Assert.AreEqual(929300, otra.Pedido);
            Assert.AreEqual(2, otra.Recogida, "4 leídas: 2 a la primera línea y 2 a esta");
            Assert.IsTrue(otra.Falta, "La unidad que falta se dio por falta");
        }

        [TestMethod]
        public void Repartir_LecturasDeshechas_NoDanNegativos()
        {
            List<LineaPickingHistoricoDTO> resultado = ServicioHistoricoAlmacen.Repartir(
                new[] { Linea(1, 1, 1, "A", 2) }, new[] { Lectura(1, "A", "Andre", -1) });

            Assert.AreEqual(0, resultado.Single().Recogida);
        }

        // ================================================================ pickings

        [TestMethod]
        public async Task LeerPickings_ResumenPorPickingConPedidosLineasYQuienLoTermino()
        {
            A.CallTo(() => repositorio.LeerPickingsConActividad(EMPRESA, DIA, DIA.AddDays(1), null, "17404", ServicioHistoricoAlmacen.MAXIMO_FILAS))
                .Returns(new List<FilaActividadHistorico>
                {
                    new FilaActividadHistorico { Numero = 99757, Fecha = DIA.AddHours(8) },
                    new FilaActividadHistorico { Numero = 99760, Fecha = DIA.AddHours(10) }
                });
            A.CallTo(() => repositorio.LeerLineasPickings(EMPRESA, A<IReadOnlyCollection<int>>._)).Returns(new List<FilaLineaHistorico>
            {
                Linea(99757, 929262, 1, "17404", 2),
                Linea(99757, 929300, 1, "22624", 1),
                Linea(99760, 929400, 1, "17404", 1)
            });
            A.CallTo(() => repositorio.LeerLecturasPickings(EMPRESA, A<IReadOnlyCollection<int>>._)).Returns(new List<FilaLecturaHistorico>
            {
                Lectura(99757, "17404", "Andre", 2),
                Lectura(99760, "17404", "Pedro", 1),
                Lectura(99760, "17404", "Santi", 0, faltas: 0)
            });
            A.CallTo(() => repositorio.LeerPickingsTerminados(EMPRESA, A<IReadOnlyCollection<int>>._)).Returns(new List<FilaTerminadaHistorico>
            {
                new FilaTerminadaHistorico { Numero = 99757, Usuario = @"NUEVAVISION\Alfredo", Fecha = DIA.AddHours(11) }
            });

            List<PickingHistoricoDTO> lista = await servicio.LeerPickings(EMPRESA, DIA, DIA.AddDays(1), null, " 17404 ");

            Assert.AreEqual(2, lista.Count);
            Assert.AreEqual(99760, lista[0].Picking, "El más reciente primero");
            PickingHistoricoDTO picking = lista[1];
            Assert.AreEqual(DIA.AddHours(8), picking.Fecha);
            Assert.AreEqual("Alfredo", picking.Usuario, "Quien lo terminó, sin dominio");
            Assert.IsTrue(picking.Terminado);
            CollectionAssert.AreEqual(new[] { 929262, 929300 }, picking.Pedidos);
            Assert.AreEqual(2, picking.Lineas);
            Assert.AreEqual(1, picking.LineasRecogidas);
            Assert.AreEqual("Pedro", lista[0].Usuario, "Sin terminar: quien más ha leído");
            Assert.IsFalse(lista[0].Terminado);
        }

        [TestMethod]
        public async Task LeerPickings_SinActividad_ListaVaciaSinMasConsultas()
        {
            A.CallTo(() => repositorio.LeerPickingsConActividad(A<string>._, A<DateTime>._, A<DateTime>._, A<int?>._, A<string>._, A<int>._))
                .Returns(new List<FilaActividadHistorico>());

            Assert.AreEqual(0, (await servicio.LeerPickings(EMPRESA, DIA, DIA.AddDays(1), 929262, null)).Count);
            A.CallTo(() => repositorio.LeerLineasPickings(A<string>._, A<IReadOnlyCollection<int>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task LeerPicking_ConLineasYLecturas_Detalle_YSinNadaNull()
        {
            A.CallTo(() => repositorio.LeerLineasPickings(EMPRESA, A<IReadOnlyCollection<int>>.That.Contains(99757))).Returns(new List<FilaLineaHistorico>
            {
                Linea(99757, 929262, 1, "17404", 2)
            });
            A.CallTo(() => repositorio.LeerLecturasPickings(EMPRESA, A<IReadOnlyCollection<int>>.That.Contains(99757))).Returns(new List<FilaLecturaHistorico>
            {
                Lectura(99757, "17404", "Andre", 1, faltas: 1)
            });
            A.CallTo(() => repositorio.LeerPickingsTerminados(EMPRESA, A<IReadOnlyCollection<int>>._)).Returns(new List<FilaTerminadaHistorico>());

            PickingHistoricoDetalleDTO detalle = await servicio.LeerPicking(EMPRESA, 99757);

            Assert.AreEqual(DIA.AddHours(8), detalle.Fecha, "La primera lectura");
            Assert.AreEqual("Andre", detalle.Usuario);
            Assert.IsFalse(detalle.Terminado);
            LineaPickingHistoricoDTO linea = detalle.Lineas.Single();
            Assert.AreEqual(1, linea.Recogida);
            Assert.IsTrue(linea.Falta);
            Assert.AreEqual("15191", linea.Cliente);
            Assert.AreEqual("PELUQUERÍA", linea.NombreCliente);

            Assert.IsNull(await servicio.LeerPicking(EMPRESA, 1), "Ni líneas ni lecturas ni terminado");
        }

        // ================================================================ packings

        [TestMethod]
        public async Task LeerPackings_CajasBultosDeLaEtiquetaYQuienHizoLasCajas()
        {
            A.CallTo(() => repositorio.LeerPackingsConActividad(EMPRESA, DIA, DIA.AddDays(1), null, ServicioHistoricoAlmacen.MAXIMO_FILAS))
                .Returns(new List<FilaActividadHistorico>
                {
                    new FilaActividadHistorico { Numero = 929262, Fecha = DIA.AddHours(12) },
                    new FilaActividadHistorico { Numero = 929300, Fecha = DIA.AddHours(13) }
                });
            A.CallTo(() => repositorio.LeerResumenPackings(EMPRESA, A<IReadOnlyCollection<int>>._)).Returns(new List<FilaResumenPackingHistorico>
            {
                new FilaResumenPackingHistorico { Pedido = 929262, Cajas = 2, UsuarioCajas = @"NUEVAVISION\Santiago", UsuarioLecturas = "Pedro", BultosEnvio = 3 },
                new FilaResumenPackingHistorico { Pedido = 929300, Cajas = 0, UsuarioLecturas = "Pedro" }
            });

            List<PackingHistoricoDTO> lista = await servicio.LeerPackings(EMPRESA, DIA, DIA.AddDays(1), null);

            Assert.AreEqual(929300, lista[0].Pedido, "El más reciente primero");
            Assert.AreEqual("Pedro", lista[0].Usuario, "Sin cajas, quien más ha leído");
            Assert.AreEqual(0, lista[0].Bultos, "Sin etiqueta");
            PackingHistoricoDTO packing = lista[1];
            Assert.AreEqual("Santiago", packing.Usuario);
            Assert.AreEqual(2, packing.Cajas);
            Assert.AreEqual(3, packing.Bultos);
            Assert.AreEqual(DIA.AddHours(12), packing.Fecha);
        }

        [TestMethod]
        public async Task LeerPacking_CajasConContenidoFotoYLasQueSoloTienenLecturas()
        {
            bultos.Add(new BultoAlmacenDTO { Pedido = 929262, Picking = 99757, Bulto = 1, Usuario = "Santiago", FechaFoto = DIA.AddHours(12), TieneFoto = true, RutaFotoPublica = "api/Almacen/Fotos/abc" });
            A.CallTo(() => repositorio.LeerContenidoPacking(EMPRESA, 929262)).Returns(new List<FilaContenidoPackingHistorico>
            {
                new FilaContenidoPackingHistorico { Bulto = 1, Picking = 99757, Producto = "17404", Nombre = "Champú", Usuario = "Santiago", Cantidad = 2, Ultima = DIA.AddHours(11) },
                new FilaContenidoPackingHistorico { Bulto = 1, Picking = 99757, Producto = "17404", Nombre = "Champú", Usuario = "Pedro", Cantidad = 1, Ultima = DIA.AddHours(11).AddMinutes(30) },
                new FilaContenidoPackingHistorico { Bulto = 1, Picking = 99757, Producto = "22624", Nombre = "Tinte", Usuario = "Pedro", Cantidad = 0, Ultima = DIA.AddHours(11) },
                new FilaContenidoPackingHistorico { Bulto = 2, Picking = 99757, Producto = "22624", Nombre = "Tinte", Usuario = @"NUEVAVISION\Pedro", Cantidad = 1, Ultima = DIA.AddHours(12).AddMinutes(10) }
            });
            A.CallTo(() => repositorio.LeerResumenPackings(EMPRESA, A<IReadOnlyCollection<int>>._)).Returns(new List<FilaResumenPackingHistorico>
            {
                new FilaResumenPackingHistorico { Pedido = 929262, Cajas = 1, UsuarioCajas = "Santiago", BultosEnvio = 2 }
            });

            PackingHistoricoDetalleDTO detalle = await servicio.LeerPacking(EMPRESA, 929262);

            Assert.AreEqual(2, detalle.Bultos);
            Assert.AreEqual("Santiago", detalle.Usuario);
            Assert.AreEqual(DIA.AddHours(11), detalle.Fecha, "Lo primero que se hizo (lectura o foto)");
            Assert.AreEqual(2, detalle.Cajas.Count);
            CajaPackingHistoricoDTO primera = detalle.Cajas[0];
            Assert.IsTrue(primera.TieneFoto);
            Assert.AreEqual("https://api.nuevavision.es/api/Almacen/Fotos/abc", primera.UrlFoto, "URL completa: Ariadna la pinta con un Image");
            Assert.AreEqual("Santiago", primera.Usuario);
            Assert.AreEqual(DIA.AddHours(12), primera.FechaHora, "La hora de la foto");
            ContenidoCajaHistoricoDTO champu = primera.Lineas.Single();
            Assert.AreEqual(3, champu.Cantidad, "Se suman los usuarios; lo deshecho (0) no sale");
            Assert.AreEqual("Santiago", champu.Usuario);
            Assert.AreEqual(DIA.AddHours(11).AddMinutes(30), champu.FechaHora);
            CajaPackingHistoricoDTO segunda = detalle.Cajas[1];
            Assert.IsFalse(segunda.TieneFoto);
            Assert.IsNull(segunda.UrlFoto);
            Assert.AreEqual("Pedro", segunda.Usuario, "Sin foto: quien metió lo último");
            Assert.AreEqual(DIA.AddHours(12).AddMinutes(10), segunda.FechaHora);
        }

        [TestMethod]
        public async Task LeerPacking_SinCajasNiLecturas_Null()
        {
            A.CallTo(() => repositorio.LeerContenidoPacking(EMPRESA, 1)).Returns(new List<FilaContenidoPackingHistorico>());

            Assert.IsNull(await servicio.LeerPacking(EMPRESA, 1));
        }

        // ================================================================ fechas

        [TestMethod]
        public void ValidarFechas_DiasEnterosHastaIncluido_PorDefectoHoy_YComoMucho31Dias()
        {
            Assert.IsNull(ServicioHistoricoAlmacen.ValidarFechas(null, null, DIA.AddHours(15), out DateTime inicio, out DateTime fin));
            Assert.AreEqual(DIA, inicio);
            Assert.AreEqual(DIA.AddDays(1), fin);

            Assert.IsNull(ServicioHistoricoAlmacen.ValidarFechas(DIA.AddDays(-30), DIA, DIA, out inicio, out fin), "31 días justos");
            Assert.AreEqual(DIA.AddDays(-30), inicio);

            StringAssert.Contains(ServicioHistoricoAlmacen.ValidarFechas(DIA.AddDays(-31), DIA, DIA, out _, out _), "31 días");
            StringAssert.Contains(ServicioHistoricoAlmacen.ValidarFechas(DIA.AddDays(1), DIA, DIA, out _, out _), "posterior");
        }

        // ================================================================ permisos

        private static IPrincipal Usuario(string nombre, params string[] roles)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, nombre) };
            claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        private sealed class ParametrosSinDefecto : ILectorParametrosUsuario
        {
            public readonly Dictionary<string, string> Valores = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public string LeerParametro(string empresa, string usuario, string parametro)
                => throw new AssertFailedException("El permiso no puede heredarse de «(defecto)»: hay que usar LeerParametroSinDefecto.");
            public string LeerParametroSinDefecto(string empresa, string usuario, string parametro)
                => parametro == PermisosHistoricoAlmacen.PARAMETRO && Valores.TryGetValue(usuario, out string valor) ? valor : null;
        }

        [TestMethod]
        public void Permisos_ParametroPropio_DireccionEInformatica_ElRestoNo()
        {
            var parametros = new ParametrosSinDefecto();
            parametros.Valores["Alfredo"] = "1 ";
            parametros.Valores["Pedro"] = "0";

            Assert.IsTrue(PermisosHistoricoAlmacen.PuedeVer(Usuario(@"NUEVAVISION\Alfredo"), parametros), "Su fila, sin dominio");
            Assert.IsFalse(PermisosHistoricoAlmacen.PuedeVer(Usuario("Pedro"), parametros));
            Assert.IsFalse(PermisosHistoricoAlmacen.PuedeVer(Usuario("Andre", "Almacén"), parametros), "Sin fila: no (ni hereda de «(defecto)»)");
            Assert.IsTrue(PermisosHistoricoAlmacen.PuedeVer(Usuario("Carlos", @"NUEVAVISION\Dirección"), parametros));
            Assert.IsTrue(PermisosHistoricoAlmacen.PuedeVer(Usuario("Informático", "Informática"), parametros));
            Assert.IsFalse(PermisosHistoricoAlmacen.PuedeVer(new ClaimsPrincipal(new ClaimsIdentity()), parametros), "Sin identificar");
        }

        // ================================================================ controlador

        private HistoricoAlmacenController Controlador(IPrincipal usuario, ParametrosSinDefecto parametros = null)
        {
            var controlador = new HistoricoAlmacenController(servicio, parametros ?? new ParametrosSinDefecto(), () => DIA)
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                User = usuario
            };
            return controlador;
        }

        private static Task<HttpResponseMessage> Ejecutar(IHttpActionResult resultado) => resultado.ExecuteAsync(System.Threading.CancellationToken.None);

        [TestMethod]
        public async Task Controlador_SinPermiso403DeUsted_FechasLargas400_DetalleSinNada404()
        {
            HttpResponseMessage prohibido = await Ejecutar(await Controlador(Usuario("Pedro", "Almacén")).GetPickings(DIA, DIA));
            Assert.AreEqual(HttpStatusCode.Forbidden, prohibido.StatusCode);
            StringAssert.Contains(await prohibido.Content.ReadAsStringAsync(), "Pídaselo a Dirección");
            Assert.AreEqual(HttpStatusCode.Forbidden, (await Ejecutar(await Controlador(Usuario("Pedro")).GetPackings())).StatusCode);
            Assert.AreEqual(HttpStatusCode.Forbidden, (await Ejecutar(await Controlador(Usuario("Pedro")).GetPicking(1))).StatusCode);
            Assert.AreEqual(HttpStatusCode.Forbidden, (await Ejecutar(await Controlador(Usuario("Pedro")).GetPacking(1))).StatusCode);
            A.CallTo(() => repositorio.LeerPickingsConActividad(A<string>._, A<DateTime>._, A<DateTime>._, A<int?>._, A<string>._, A<int>._))
                .MustNotHaveHappened();

            HttpResponseMessage largas = await Ejecutar(await Controlador(Usuario("Carlos", "Dirección")).GetPackings(DIA.AddDays(-40), DIA));
            Assert.AreEqual(HttpStatusCode.BadRequest, largas.StatusCode);

            A.CallTo(() => repositorio.LeerContenidoPacking(EMPRESA, 5)).Returns(new List<FilaContenidoPackingHistorico>());
            HttpResponseMessage noHay = await Ejecutar(await Controlador(Usuario("Carlos", "Dirección")).GetPacking(5));
            Assert.AreEqual(HttpStatusCode.NotFound, noHay.StatusCode);
        }

        [TestMethod]
        public async Task Controlador_ConPermiso_DevuelveLaListaConLasFechasDelDia()
        {
            var parametros = new ParametrosSinDefecto();
            parametros.Valores["Santiago"] = "1";
            A.CallTo(() => repositorio.LeerPickingsConActividad(EMPRESA, DIA, DIA.AddDays(1), 929262, null, A<int>._))
                .Returns(new List<FilaActividadHistorico> { new FilaActividadHistorico { Numero = 99757, Fecha = DIA } });

            var ok = await Controlador(Usuario("Santiago"), parametros).GetPickings(null, null, 929262) as System.Web.Http.Results.OkNegotiatedContentResult<List<PickingHistoricoDTO>>;

            Assert.IsNotNull(ok);
            Assert.AreEqual(99757, ok.Content.Single().Picking);
        }
    }
}
