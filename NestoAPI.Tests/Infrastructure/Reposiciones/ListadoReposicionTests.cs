using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Infraestructure.Reposiciones;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using RecepcionReposiciones = NestoAPI.Infraestructure.PreparacionAlmacen.IServicioRecepcionReposiciones;

namespace NestoAPI.Tests.Infrastructure.Reposiciones
{
    /// <summary>
    /// Sugerencia 564 (Paloma): la reposición en papel para prepararla o comprobarla a mano. Qué se imprime (sin las
    /// líneas a 0), en qué orden (por ubicación si la hay; si no, por familia y descripción), la cabecera y los endpoints.
    /// </summary>
    [TestClass]
    public class ListadoReposicionTests
    {
        private IServicioPreparacionReposicion preparacion;
        private RecepcionReposiciones recepcion;
        private IRepositorioListadoReposicion repositorio;
        private ServicioListadoReposicion servicio;

        [TestInitialize]
        public void Preparar()
        {
            preparacion = A.Fake<IServicioPreparacionReposicion>();
            recepcion = A.Fake<RecepcionReposiciones>();
            repositorio = A.Fake<IRepositorioListadoReposicion>();
            A.CallTo(() => repositorio.LeerFichas(A<string>._, A<string>._, A<IReadOnlyCollection<string>>._))
                .Returns(new List<FichaListadoReposicion>());
            servicio = new ServicioListadoReposicion(preparacion, recepcion, repositorio);
        }

        private static LineaListadoReposicionDTO Linea(string producto, string descripcion, string familia = null,
            string pasillo = null, string fila = null, string columna = null)
            => new LineaListadoReposicionDTO
            {
                Producto = producto, Descripcion = descripcion, Familia = familia, Pasillo = pasillo, Fila = fila, Columna = columna, Cantidad = 1
            };

        private static ReposicionEnPreparacionDTO EnPreparacion() => new ReposicionEnPreparacionDTO
        {
            Empresa = "1",
            Origen = "ALC",
            Destino = "ALG",
            NombreOrigen = "Alcobendas",
            NombreDestino = "Algete",
            Fecha = new DateTime(2026, 10, 9),
            Lineas = new List<LineaReposicionEnPreparacionDTO>
            {
                new LineaReposicionEnPreparacionDTO { NumeroOrden = 1, Producto = "17404 ", Nombre = "Cera tibia", CodigoBarras = "8411", Cantidad = 3 },
                new LineaReposicionEnPreparacionDTO { NumeroOrden = 2, Producto = "40057", Nombre = "Banda", CodigoBarras = "8422", Cantidad = 0 },
                new LineaReposicionEnPreparacionDTO { NumeroOrden = 3, Producto = "25539", Nombre = "Aceite", CodigoBarras = "8433", Cantidad = 2 }
            }
        };

        // ---------------------------------------------------------------- orden

        [TestMethod]
        public void OrdenarParaRecorrer_ConUbicacionPrimeroEnElOrdenDelRecorridoYLuegoPorFamiliaYDescripcion()
        {
            var lineas = new List<LineaListadoReposicionDTO>
            {
                Linea("1", "Zumo", "Cosmética"),
                Linea("2", "Sin familia"),
                Linea("3", "Hueco 2", "Uñas", "002", "001", "001"),
                Linea("4", "Aceite", "cosmética"),
                Linea("5", "Hueco 1 col 2", "Aparatos", "001", "001", "002"),
                Linea("6", "Hueco 1 col 1 fila 3", "Aparatos", "001", "003", "001"),
                Linea("7", "Banda", "Aparatología")
            };

            List<LineaListadoReposicionDTO> ordenadas = ServicioListadoReposicion.OrdenarParaRecorrer(lineas);

            CollectionAssert.AreEqual(new[] { "6", "5", "3", "7", "4", "1", "2" }, ordenadas.Select(l => l.Producto).ToArray());
        }

        [TestMethod]
        public void OrdenarParaRecorrer_SinUbicacionesEnLaTienda_PorFamiliaYDescripcionSinDistinguirMayusculasNiTildes()
        {
            var lineas = new List<LineaListadoReposicionDTO>
            {
                Linea("1", "esmalte rojo", "Uñas"),
                Linea("2", "Ácido", "Cosmética"),
                Linea("3", "Esmalte azul", "uñas"),
                Linea("4", "Crema", "Cosmética")
            };

            List<LineaListadoReposicionDTO> ordenadas = ServicioListadoReposicion.OrdenarParaRecorrer(lineas);

            CollectionAssert.AreEqual(new[] { "2", "4", "3", "1" }, ordenadas.Select(l => l.Producto).ToArray());
        }

        // ---------------------------------------------------------------- enviar

        [TestMethod]
        public async Task LeerEnPreparacion_SinLasLineasACeroConFamiliaYHuecoDelOrigenYElCorte()
        {
            A.CallTo(() => preparacion.LeerEnPreparacion("1", "ALC")).Returns(EnPreparacion());
            IReadOnlyCollection<string> pedidos = null;
            A.CallTo(() => repositorio.LeerFichas("1", "ALC", A<IReadOnlyCollection<string>>._))
                .ReturnsLazily((string e, string a, IReadOnlyCollection<string> p) =>
                {
                    pedidos = p;
                    return Task.FromResult(new List<FichaListadoReposicion>
                    {
                        new FichaListadoReposicion { Producto = "17404", Familia = "Depilación " },
                        new FichaListadoReposicion { Producto = "25539", Familia = "Cosmética", Pasillo = "001", Fila = "002", Columna = "003" }
                    });
                });
            A.CallTo(() => repositorio.LeerFechaCorteEnPreparacion("1", "ALC", "ALG")).Returns(new DateTime(2026, 10, 9, 9, 0, 0));

            ListadoReposicionDTO listado = await servicio.LeerEnPreparacion("1 ", "alc");

            Assert.IsTrue(listado.EsEnvio);
            Assert.AreEqual("Alcobendas (ALC) → Algete (ALG)", listado.Ruta);
            Assert.AreEqual("Reposición para preparar", listado.Titulo);
            Assert.AreEqual("ALC", listado.AlmacenUbicaciones);
            Assert.IsNull(listado.NumTraspaso);
            Assert.AreEqual(new DateTime(2026, 10, 9, 9, 0, 0), listado.FechaCorte);
            CollectionAssert.AreEquivalent(new[] { "17404", "25539" }, pedidos.ToArray());
            Assert.AreEqual(2, listado.Lineas.Count, "La línea a 0 no se manda: no se imprime");
            Assert.AreEqual("25539", listado.Lineas[0].Producto, "Lo que tiene hueco va primero");
            Assert.AreEqual("001/002/003", listado.Lineas[0].Ubicacion);
            Assert.AreEqual("17404", listado.Lineas[1].Producto);
            Assert.AreEqual("Depilación", listado.Lineas[1].Familia);
            Assert.AreEqual("8411", listado.Lineas[1].CodigoBarras);
            Assert.AreEqual("Cera tibia", listado.Lineas[1].Descripcion);
            Assert.AreEqual(3, listado.Lineas[1].Cantidad);
            Assert.AreEqual(5, listado.Unidades);
        }

        [TestMethod]
        public async Task LeerEnPreparacion_SinNingunaEnPreparacion_Null()
        {
            A.CallTo(() => preparacion.LeerEnPreparacion(A<string>._, A<string>._)).Returns((ReposicionEnPreparacionDTO)null);

            Assert.IsNull(await servicio.LeerEnPreparacion("1", "ALC"));
        }

        [TestMethod]
        public async Task LeerEnPreparacion_SiFallaLeerElCorte_SeImprimeIgualSinCorte()
        {
            A.CallTo(() => preparacion.LeerEnPreparacion("1", "ALC")).Returns(EnPreparacion());
            A.CallTo(() => repositorio.LeerFechaCorteEnPreparacion(A<string>._, A<string>._, A<string>._)).Throws(new InvalidOperationException("sin tabla"));

            ListadoReposicionDTO listado = await servicio.LeerEnPreparacion("1", "ALC");

            Assert.IsNull(listado.FechaCorte);
            Assert.AreEqual(2, listado.Lineas.Count);
        }

        // ---------------------------------------------------------------- recibir

        [TestMethod]
        public async Task LeerRecepcion_OrigenDeLasPendientesUbicacionesDelDestinoYCorteDelTraspaso()
        {
            A.CallTo(() => recepcion.LeerRecepcion("1", "ALC", 80893)).Returns(new RecepcionReposicionDTO
            {
                Traspaso = 80893,
                Lineas = new List<LineaReposicionDTO>
                {
                    new LineaReposicionDTO { Producto = "17404", Descripcion = "Cera", CodigoBarras = "8411", Cantidad = 4 },
                    new LineaReposicionDTO { Producto = "40057", Descripcion = "Banda", CodigoBarras = null, SinCodigo = true, Cantidad = 1 }
                }
            });
            A.CallTo(() => recepcion.LeerPendientes("1", "ALC")).Returns(new List<ReposicionPendienteDTO>
            {
                new ReposicionPendienteDTO { Traspaso = 80890, Origen = "REI", NombreOrigen = "Reina" },
                new ReposicionPendienteDTO { Traspaso = 80893, Origen = "ALG", NombreOrigen = "Algete", Fecha = new DateTime(2026, 10, 8) }
            });
            A.CallTo(() => repositorio.LeerNombreAlmacen("1", "ALC")).Returns("Alcobendas");
            A.CallTo(() => repositorio.LeerFechaCorteTraspaso("1", 80893)).Returns(new DateTime(2026, 10, 7, 18, 0, 0));
            A.CallTo(() => repositorio.LeerFichas("1", "ALC", A<IReadOnlyCollection<string>>._)).Returns(new List<FichaListadoReposicion>
            {
                new FichaListadoReposicion { Producto = "17404", Familia = "Depilación" },
                new FichaListadoReposicion { Producto = "40057", Familia = "Aparatología" }
            });

            ListadoReposicionDTO listado = await servicio.LeerRecepcion("1", "alc", 80893);

            Assert.IsFalse(listado.EsEnvio);
            Assert.AreEqual("Reposición para recibir", listado.Titulo);
            Assert.AreEqual("Algete (ALG) → Alcobendas (ALC)", listado.Ruta);
            Assert.AreEqual("ALC", listado.AlmacenUbicaciones);
            Assert.AreEqual(80893, listado.NumTraspaso);
            Assert.AreEqual(new DateTime(2026, 10, 8), listado.Fecha);
            Assert.AreEqual(new DateTime(2026, 10, 7, 18, 0, 0), listado.FechaCorte);
            CollectionAssert.AreEqual(new[] { "40057", "17404" }, listado.Lineas.Select(l => l.Producto).ToArray(), "Por familia");
        }

        [TestMethod]
        public async Task LeerRecepcion_QueNoEstaPendiente_Null()
        {
            A.CallTo(() => recepcion.LeerRecepcion(A<string>._, A<string>._, A<int>._)).Returns((RecepcionReposicionDTO)null);

            Assert.IsNull(await servicio.LeerRecepcion("1", "ALC", 1));
        }

        // ---------------------------------------------------------------- PDF

        [TestMethod]
        public void Filas_MantienenElOrdenDelListadoConLosTextosDeCadaColumna()
        {
            var listado = new ListadoReposicionDTO
            {
                Lineas = new List<LineaListadoReposicionDTO>
                {
                    new LineaListadoReposicionDTO { Producto = "25539", CodigoBarras = "8433", Descripcion = "Aceite", Familia = "Cosmética", Cantidad = 2, Pasillo = "001", Fila = "002", Columna = "003" },
                    new LineaListadoReposicionDTO { Producto = "17404", CodigoBarras = null, Descripcion = "Cera", Cantidad = 3 }
                }
            };

            List<FilaPdfReposicion> filas = GeneradorPdfReposicion.Filas(listado);

            Assert.AreEqual(2, filas.Count);
            Assert.AreEqual("001/002/003", filas[0].Ubicacion);
            Assert.AreEqual("25539", filas[0].Producto);
            Assert.AreEqual("8433", filas[0].CodigoBarras);
            Assert.AreEqual("Aceite", filas[0].Descripcion);
            Assert.AreEqual(2, filas[0].Cantidad);
            Assert.AreEqual(string.Empty, filas[1].Ubicacion);
            Assert.AreEqual(string.Empty, filas[1].CodigoBarras);
            Assert.AreEqual("17404", filas[1].Producto);
        }

        [TestMethod]
        public void LineaDatos_ConNumeroDiaYCorte_YSinNumeroNiCorte()
        {
            Assert.AreEqual("Traspaso 80893 · Día 08/10/2026 · Corte 07/10/2026 18:00", GeneradorPdfReposicion.LineaDatos(new ListadoReposicionDTO
            {
                NumTraspaso = 80893, Fecha = new DateTime(2026, 10, 8), FechaCorte = new DateTime(2026, 10, 7, 18, 0, 0)
            }));
            Assert.AreEqual("Sin número de traspaso todavía (se pone al terminar) · Día 09/10/2026 · Corte: a mano",
                GeneradorPdfReposicion.LineaDatos(new ListadoReposicionDTO { Fecha = new DateTime(2026, 10, 9) }));
        }

        [TestMethod]
        public void GenerarPdf_ConYSinUbicacionesYNull_DevuelvePdfValido()
        {
            var conUbicacion = new ListadoReposicionDTO
            {
                EsEnvio = true, Origen = "ALC", Destino = "ALG",
                Lineas = Enumerable.Range(1, 120).Select(i => new LineaListadoReposicionDTO
                {
                    Producto = i.ToString(), Descripcion = "Producto con una descripción bastante larga para que ocupe dos líneas " + i,
                    CodigoBarras = "84" + i, Cantidad = i, Pasillo = i % 2 == 0 ? "001" : null, Fila = "001", Columna = "001"
                }).ToList()
            };
            var sinUbicacion = new ListadoReposicionDTO
            {
                Lineas = new List<LineaListadoReposicionDTO> { new LineaListadoReposicionDTO { Producto = "1", Descripcion = "A", Cantidad = 1 } }
            };

            foreach (ListadoReposicionDTO listado in new[] { conUbicacion, sinUbicacion, null })
            {
                byte[] bytes = new GeneradorPdfReposicion().GenerarPdf(listado).ReadAsByteArrayAsync().Result;
                Assert.IsTrue(bytes.Length > 4);
                Assert.AreEqual("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
            }
        }

        // ---------------------------------------------------------------- endpoints

        private static ReposicionesController Controlador(IServicioListadoReposicion listado)
            => new ReposicionesController(null, A.Fake<IServicioPreparacionReposicion>(), listado: listado)
            {
                Request = new HttpRequestMessage(),
                Configuration = new System.Web.Http.HttpConfiguration()
            };

        [TestMethod]
        public async Task GetEnPreparacionPdf_DevuelveElPdfDeLaDelOrigen()
        {
            IServicioListadoReposicion listado = A.Fake<IServicioListadoReposicion>();
            A.CallTo(() => listado.LeerEnPreparacion("1", "ALC")).Returns(new ListadoReposicionDTO { EsEnvio = true, Origen = "ALC", Destino = "ALG" });

            HttpResponseMessage respuesta = await Controlador(listado).GetEnPreparacionPdf("ALC");

            Assert.AreEqual(HttpStatusCode.OK, respuesta.StatusCode);
            Assert.AreEqual("application/pdf", respuesta.Content.Headers.ContentType.MediaType);
        }

        [TestMethod]
        public async Task GetEnPreparacionPdf_SinNinguna_404()
        {
            IServicioListadoReposicion listado = A.Fake<IServicioListadoReposicion>();
            A.CallTo(() => listado.LeerEnPreparacion(A<string>._, A<string>._)).Returns((ListadoReposicionDTO)null);

            HttpResponseMessage respuesta = await Controlador(listado).GetEnPreparacionPdf("ALC");

            Assert.AreEqual(HttpStatusCode.NotFound, respuesta.StatusCode);
        }

        [TestMethod]
        public async Task GetRecepcionPdf_DevuelveElPdfYSiNoEstaPendiente404()
        {
            IServicioListadoReposicion listado = A.Fake<IServicioListadoReposicion>();
            A.CallTo(() => listado.LeerRecepcion("1", "ALC", 80893)).Returns(new ListadoReposicionDTO { NumTraspaso = 80893 });
            A.CallTo(() => listado.LeerRecepcion("1", "ALC", 1)).Returns((ListadoReposicionDTO)null);

            HttpResponseMessage ok = await Controlador(listado).GetRecepcionPdf(80893, "ALC");
            HttpResponseMessage noEsta = await Controlador(listado).GetRecepcionPdf(1, "ALC");

            Assert.AreEqual(HttpStatusCode.OK, ok.StatusCode);
            Assert.AreEqual("application/pdf", ok.Content.Headers.ContentType.MediaType);
            Assert.AreEqual(HttpStatusCode.NotFound, noEsta.StatusCode);
        }
    }
}
