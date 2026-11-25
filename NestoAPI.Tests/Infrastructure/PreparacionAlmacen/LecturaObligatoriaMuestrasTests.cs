using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Clientes.Embudo;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Infraestructure.Productos;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>
    /// Carlos 25/11/26: en el picking (PICK y REPO), leer la etiqueta de la muestra es obligatorio con el interruptor del almacén
    /// encendido (ParametrosUsuario «(defecto)» Muestras.PickingLecturaObligatoria.{ALG|REI|ALC} = «1»). Apagado = exactamente como hoy.
    /// </summary>
    [TestClass]
    public class LecturaObligatoriaMuestrasTests
    {
        private const string FABRICANTE = "4006381333931";
        private static readonly string INTERNO_45929 = CodigoBarrasInterno.DeReferencia("45929");

        /// <summary>Una parada como sale de la API: ordenada (OrdenarRecorrido) y con los códigos completos (CompletadorCodigosBarras).</summary>
        private static LineaRecogidaDTO Parada(string producto, string subGrupo, string codigoFicha, params string[] activos)
        {
            var linea = new LineaRecogidaDTO
            {
                Producto = producto,
                Descripcion = "PRODUCTO " + producto,
                CodigoBarras = codigoFicha,
                SubGrupoProducto = subGrupo,
                Cantidad = 2,
                Pasillo = "010",
                Fila = "002",
                Columna = "003"
            };
            _ = CasadorEscaneos.OrdenarRecorrido(new List<LineaPickingAlmacenDTO> { linea });
            linea.CodigosBarras = CompletadorCodigosBarras.Juntar(linea.CodigoBarras, activos);
            return linea;
        }

        private static string Foto(LineaPickingAlmacenDTO l)
            => string.Join("|", l.CodigoBarras, string.Join(",", l.CodigosBarras), l.SinCodigo, l.SinCodigoAdmitido, l.PideCantidad,
                l.CodigoDuplicado, l.LecturaObligatoria);

        [TestMethod]
        public void Interpretar_SoloElUnoEnciende()
        {
            Assert.IsTrue(ReglaLecturaObligatoriaMuestras.Interpretar("1"));
            Assert.IsTrue(ReglaLecturaObligatoriaMuestras.Interpretar(" 1 "));
            foreach (string valor in new[] { null, "", " ", "0", "true", "sí", "si", "encendido", "11" })
            {
                Assert.IsFalse(ReglaLecturaObligatoriaMuestras.Interpretar(valor), valor ?? "(null)");
            }
        }

        [TestMethod]
        public void Clave_PorAlmacen()
        {
            Assert.AreEqual("Muestras.PickingLecturaObligatoria.ALG", ReglaLecturaObligatoriaMuestras.Clave(" alg "));
            Assert.AreEqual("Muestras.PickingLecturaObligatoria.REI", ReglaLecturaObligatoriaMuestras.Clave("REI"));
            Assert.AreEqual("Muestras.PickingLecturaObligatoria.ALC", ReglaLecturaObligatoriaMuestras.Clave("ALC"));
            Assert.IsNull(ReglaLecturaObligatoriaMuestras.Clave("AMZ"));
            Assert.IsNull(ReglaLecturaObligatoriaMuestras.Clave(null));
        }

        [TestMethod]
        public void Aplicar_Apagado_NoTocaNada()
        {
            var lineas = new List<LineaPickingAlmacenDTO>
            {
                Parada("45929", "MMP", null, INTERNO_45929),
                Parada("45930", "MMP", null),
                Parada("17404", "ACP", FABRICANTE)
            };
            List<string> antes = lineas.Select(Foto).ToList();

            Assert.AreEqual(0, ReglaLecturaObligatoriaMuestras.Aplicar(lineas, false));

            CollectionAssert.AreEqual(antes, lineas.Select(Foto).ToList());
            // Como hoy: la muestra con solo el interno sigue «admitida» y sin código en la ficha
            Assert.IsTrue(lineas[0].SinCodigoAdmitido);
            Assert.IsTrue(lineas[0].SinCodigo);
            Assert.IsFalse(lineas[0].LecturaObligatoria);
        }

        [TestMethod]
        public void Aplicar_Encendido_MuestraConSoloElInterno_HayQueLeerlo()
        {
            LineaRecogidaDTO muestra = Parada("45929", "MMP", null, INTERNO_45929);

            Assert.AreEqual(1, ReglaLecturaObligatoriaMuestras.Aplicar(new[] { muestra }, true));

            Assert.IsTrue(muestra.LecturaObligatoria);
            Assert.IsFalse(muestra.SinCodigoAdmitido);
            Assert.IsFalse(muestra.SinCodigo);
            Assert.AreEqual(INTERNO_45929, muestra.CodigoBarras);
            CollectionAssert.AreEqual(new[] { INTERNO_45929 }, muestra.CodigosBarras);
            Assert.IsTrue(muestra.PideCantidad, "un bip no es una unidad");
            Assert.IsFalse(muestra.CodigoDuplicado);
        }

        [TestMethod]
        public void Aplicar_Encendido_MuestraConCodigoDelFabricante_SeLeeYConservaSuPrincipal()
        {
            LineaRecogidaDTO muestra = Parada("45929", "MMP", FABRICANTE, INTERNO_45929);

            Assert.AreEqual(1, ReglaLecturaObligatoriaMuestras.Aplicar(new[] { muestra }, true));

            Assert.IsTrue(muestra.LecturaObligatoria);
            Assert.IsFalse(muestra.SinCodigoAdmitido);
            Assert.AreEqual(FABRICANTE, muestra.CodigoBarras);
            CollectionAssert.AreEqual(new[] { FABRICANTE, INTERNO_45929 }, muestra.CodigosBarras);
        }

        [TestMethod]
        public void Aplicar_Encendido_MuestraSinNingunCodigo_SigueTecleandose()
        {
            LineaRecogidaDTO sinNada = Parada("45930", "MMP", null);
            LineaRecogidaDTO soloCodigoQueNoVale = Parada("45931", "MMP", null, "123");
            string antes1 = Foto(sinNada), antes2 = Foto(soloCodigoQueNoVale);

            Assert.AreEqual(0, ReglaLecturaObligatoriaMuestras.Aplicar(new[] { sinNada, soloCodigoQueNoVale }, true));

            Assert.AreEqual(antes1, Foto(sinNada));
            Assert.AreEqual(antes2, Foto(soloCodigoQueNoVale));
            Assert.IsTrue(sinNada.SinCodigoAdmitido);
            Assert.IsTrue(soloCodigoQueNoVale.SinCodigoAdmitido);
        }

        [TestMethod]
        public void Aplicar_Encendido_LoQueNoEsMuestra_NoCambia()
        {
            LineaRecogidaDTO conCodigo = Parada("17404", "ACP", FABRICANTE);
            LineaRecogidaDTO sinCodigo = Parada("17405", "ACP", null);
            LineaRecogidaDTO soloAlternativo = Parada("17406", "ACP", null, CodigoBarrasInterno.DeReferencia("17406"));
            List<string> antes = new[] { conCodigo, sinCodigo, soloAlternativo }.Select(Foto).ToList();

            Assert.AreEqual(0, ReglaLecturaObligatoriaMuestras.Aplicar(new[] { conCodigo, sinCodigo, soloAlternativo }, true));

            CollectionAssert.AreEqual(antes, new[] { conCodigo, sinCodigo, soloAlternativo }.Select(Foto).ToList());
        }

        [TestMethod]
        public void Aplicar_Encendido_PrincipalNuevoCompartido_MarcaElDuplicado()
        {
            // Un código de la tabla de la muestra que es el principal de otro producto del mismo picking
            LineaRecogidaDTO otro = Parada("17404", "ACP", FABRICANTE);
            LineaRecogidaDTO muestra = Parada("45929", "MMP", null, FABRICANTE);

            _ = ReglaLecturaObligatoriaMuestras.Aplicar(new[] { otro, muestra }, true);

            Assert.IsTrue(muestra.CodigoDuplicado);
            Assert.IsTrue(otro.CodigoDuplicado);
        }

        [TestMethod]
        public void AvisoMuestrasSinCodigo_SoloAlEncenderYConAlguna()
        {
            Assert.IsNull(ReglaLecturaObligatoriaMuestras.AvisoMuestrasSinCodigo("ALG", true, 0));
            Assert.IsNull(ReglaLecturaObligatoriaMuestras.AvisoMuestrasSinCodigo("ALG", false, 5));
            StringAssert.StartsWith(ReglaLecturaObligatoriaMuestras.AvisoMuestrasSinCodigo("ALG", true, 12), "En Algete quedan 12 muestras con stock sin ningún código");
            StringAssert.Contains(ReglaLecturaObligatoriaMuestras.AvisoMuestrasSinCodigo("REI", true, 1), "En Reina queda 1 muestra");
        }

        // ---- Interruptor ----

        [TestMethod]
        public async Task Interruptor_SinFila_Apagado_YConCacheCorta()
        {
            IParametrosEmbudoDiario parametros = A.Fake<IParametrosEmbudoDiario>();
            A.CallTo(() => parametros.Leer("Muestras.PickingLecturaObligatoria.ALG")).Returns(null);
            DateTime ahora = new DateTime(2026, 11, 25, 10, 0, 0);
            var interruptor = new InterruptorLecturaMuestras(parametros, () => ahora);

            Assert.IsFalse(await interruptor.Encendido("ALG"));
            A.CallTo(() => parametros.Leer("Muestras.PickingLecturaObligatoria.ALG")).Returns("1");
            Assert.IsFalse(await interruptor.Encendido("ALG"), "dentro de la caché");
            ahora = ahora.AddMinutes(3);
            Assert.IsTrue(await interruptor.Encendido("ALG"), "caducada la caché");
            A.CallTo(() => parametros.Leer(A<string>._)).MustHaveHappenedTwiceExactly();
        }

        [TestMethod]
        public async Task Interruptor_AlmacenQueNoVale_ApagadoSinLeer()
        {
            IParametrosEmbudoDiario parametros = A.Fake<IParametrosEmbudoDiario>();
            var interruptor = new InterruptorLecturaMuestras(parametros);

            Assert.IsFalse(await interruptor.Encendido("AMZ"));
            Assert.IsFalse(await interruptor.Encendido(null));
            A.CallTo(() => parametros.Leer(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Interruptor_Cambiar_EscribeUnoOCeroEInvalida()
        {
            IParametrosEmbudoDiario parametros = A.Fake<IParametrosEmbudoDiario>();
            A.CallTo(() => parametros.Leer(A<string>._)).Returns(null);
            var interruptor = new InterruptorLecturaMuestras(parametros);
            Assert.IsFalse(await interruptor.Encendido("REI"));

            await interruptor.Cambiar("rei", true, "NUEVAVISION\\Carlos");
            A.CallTo(() => parametros.Escribir("Muestras.PickingLecturaObligatoria.REI", "1", "NUEVAVISION\\Carlos", A<string>._, true)).MustHaveHappenedOnceExactly();
            A.CallTo(() => parametros.Leer("Muestras.PickingLecturaObligatoria.REI")).Returns("1");
            Assert.IsTrue(await interruptor.Encendido("REI"), "la caché se ha invalidado");

            await interruptor.Cambiar("REI", false, "Carlos");
            A.CallTo(() => parametros.Escribir("Muestras.PickingLecturaObligatoria.REI", "0", "Carlos", A<string>._, true)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Interruptor_SiNoSePuedeLeer_Apagado()
        {
            IParametrosEmbudoDiario parametros = A.Fake<IParametrosEmbudoDiario>();
            A.CallTo(() => parametros.Leer(A<string>._)).Throws(new InvalidOperationException("sin base de datos"));

            Assert.IsFalse(await new InterruptorLecturaMuestras(parametros).Encendido("ALG"));
        }

        // ---- GET/PUT ----

        private static MuestraAlmacen Muestra(string producto, int stock, params string[] codigos) => new MuestraAlmacen
        {
            Producto = producto,
            SubGrupo = "MMP",
            Estado = 0,
            Stock = stock,
            Codigos = codigos.Select(c => new CodigoBarrasProductoDTO { Codigo = c, Activo = true }).ToList()
        };

        [TestMethod]
        public async Task Servicio_Cambiar_AlEncenderAvisaDeLasQueSiguenSinCodigo()
        {
            IInterruptorLecturaMuestras interruptor = A.Fake<IInterruptorLecturaMuestras>();
            A.CallTo(() => interruptor.Encendido("ALG", true)).Returns(false);
            IRepositorioMuestras repositorio = A.Fake<IRepositorioMuestras>();
            A.CallTo(() => repositorio.LeerMuestras("1", "ALG")).Returns(new List<MuestraAlmacen>
            {
                Muestra("45929", 3, INTERNO_45929),
                Muestra("45930", 2),
                Muestra("45931", 0),          // sin stock: no cuenta
                Muestra("45932", 1, FABRICANTE)
            });
            var servicio = new ServicioLecturaObligatoriaMuestras(interruptor, repositorio);

            ResultadoCambioLecturaObligatoriaMuestrasDTO resultado = await servicio.Cambiar("1",
                new CambioLecturaObligatoriaMuestrasDTO { Almacen = "alg", Encendido = true }, "Carlos");

            A.CallTo(() => interruptor.Cambiar("ALG", true, "Carlos")).MustHaveHappenedOnceExactly();
            Assert.IsTrue(resultado.Cambiado);
            Assert.AreEqual("ALG", resultado.Almacen);
            Assert.AreEqual(2, resultado.MuestrasConCodigo);
            Assert.AreEqual(1, resultado.MuestrasSinCodigo);
            StringAssert.StartsWith(resultado.Aviso, "En Algete queda 1 muestra con stock sin ningún código");
        }

        [TestMethod]
        public async Task Servicio_Cambiar_SiYaEstabaAsi_NoEscribe()
        {
            IInterruptorLecturaMuestras interruptor = A.Fake<IInterruptorLecturaMuestras>();
            A.CallTo(() => interruptor.Encendido("REI", true)).Returns(true);
            var servicio = new ServicioLecturaObligatoriaMuestras(interruptor, A.Fake<IRepositorioMuestras>());

            ResultadoCambioLecturaObligatoriaMuestrasDTO resultado = await servicio.Cambiar("1",
                new CambioLecturaObligatoriaMuestrasDTO { Almacen = "REI", Encendido = true }, "Carlos");

            Assert.IsFalse(resultado.Cambiado);
            A.CallTo(() => interruptor.Cambiar(A<string>._, A<bool>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Servicio_Cambiar_AlmacenQueNoVale_400()
        {
            var servicio = new ServicioLecturaObligatoriaMuestras(A.Fake<IInterruptorLecturaMuestras>(), A.Fake<IRepositorioMuestras>());
            _ = await Assert.ThrowsExceptionAsync<ArgumentException>(() => servicio.Cambiar("1",
                new CambioLecturaObligatoriaMuestrasDTO { Almacen = "AMZ", Encendido = true }, "Carlos"));
        }

        [TestMethod]
        public async Task Servicio_Leer_UnaFilaPorAlmacen()
        {
            IInterruptorLecturaMuestras interruptor = A.Fake<IInterruptorLecturaMuestras>();
            A.CallTo(() => interruptor.Encendido("ALG", true)).Returns(true);
            var servicio = new ServicioLecturaObligatoriaMuestras(interruptor, A.Fake<IRepositorioMuestras>());

            List<EstadoLecturaObligatoriaMuestrasDTO> estados = await servicio.Leer("1");

            CollectionAssert.AreEqual(new[] { "ALG", "REI", "ALC" }, estados.Select(e => e.Almacen).ToList());
            CollectionAssert.AreEqual(new[] { true, false, false }, estados.Select(e => e.Encendido).ToList());
        }

        // ---- Sin etiqueta en el hueco ----

        private static (ServicioMuestraSinEtiquetaSalida Servicio, IAvisadorMuestraSinEtiqueta Avisador, IInterruptorLecturaMuestras Interruptor)
            ServicioSinEtiqueta(RecogidaAlmacenDTO recogida, bool encendido = true)
        {
            IServicioPreparacionAlmacen salidas = A.Fake<IServicioPreparacionAlmacen>();
            A.CallTo(() => salidas.LeerRecogida("1", "PICK", 100070)).Returns(recogida);
            IInterruptorLecturaMuestras interruptor = A.Fake<IInterruptorLecturaMuestras>();
            A.CallTo(() => interruptor.Encendido("ALG", A<bool>._)).Returns(encendido);
            IAvisadorMuestraSinEtiqueta avisador = A.Fake<IAvisadorMuestraSinEtiqueta>();
            A.CallTo(() => avisador.Avisar(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<int>._))
                .Returns(true);
            return (new ServicioMuestraSinEtiquetaSalida(salidas, interruptor, avisador, new MemoriaAvisosMuestraSinEtiqueta(),
                () => new DateTime(2026, 11, 25, 9, 0, 0)), avisador, interruptor);
        }

        private static RecogidaAlmacenDTO Recogida(params LineaRecogidaDTO[] lineas)
            => new RecogidaAlmacenDTO { Empresa = "1", Almacen = "ALG", Tipo = "PICK", Numero = 100070, Lineas = lineas.ToList() };

        private static MuestraSinEtiquetaSalidaDTO Peticion(string producto = "45929", string motivo = "NoSeLee")
            => new MuestraSinEtiquetaSalidaDTO { Producto = producto, Pasillo = "010", Fila = "002", Columna = "003", Motivo = motivo };

        [TestMethod]
        public async Task SinEtiqueta_AvisaAAlmacenUnaVezAlDia()
        {
            var (servicio, avisador, _) = ServicioSinEtiqueta(Recogida(Parada("45929", "MMP", null, INTERNO_45929)));

            ResultadoMuestraSinEtiquetaSalidaDTO primera = await servicio.Apuntar("1", "pick", 100070, Peticion(), "NUEVAVISION\\Pedro");
            ResultadoMuestraSinEtiquetaSalidaDTO segunda = await servicio.Apuntar("1", "PICK", 100070, Peticion(motivo: "NoLleva"), "NUEVAVISION\\Andre");

            Assert.IsTrue(primera.Avisado);
            Assert.AreEqual("010/002/003", primera.Hueco);
            StringAssert.StartsWith(primera.Mensaje, "Cuente la muestra a mano.");
            Assert.IsFalse(segunda.Avisado);
            Assert.IsTrue(segunda.YaAvisadoHoy);
            A.CallTo(() => avisador.Avisar("1", "ALG", "45929", A<string>._, "010/002/003", "NUEVAVISION\\Pedro", "la etiqueta no se lee", "PICK", 100070))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task SinEtiqueta_ConElInterruptorApagado_400()
        {
            var (servicio, avisador, _) = ServicioSinEtiqueta(Recogida(Parada("45929", "MMP", null, INTERNO_45929)), encendido: false);

            ArgumentException ex = await Assert.ThrowsExceptionAsync<ArgumentException>(() => servicio.Apuntar("1", "PICK", 100070, Peticion(), "Pedro"));
            StringAssert.Contains(ex.Message, "no es obligatorio");
            A.CallTo(avisador).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task SinEtiqueta_LoQueNoEsMuestra_MotivoMaloOFueraDeLaRecogida_400()
        {
            var (servicio, avisador, _) = ServicioSinEtiqueta(Recogida(Parada("17404", "ACP", FABRICANTE), Parada("45929", "MMP", null, INTERNO_45929)));

            _ = await Assert.ThrowsExceptionAsync<ArgumentException>(() => servicio.Apuntar("1", "PICK", 100070, Peticion("17404"), "Pedro"));
            _ = await Assert.ThrowsExceptionAsync<ArgumentException>(() => servicio.Apuntar("1", "PICK", 100070, Peticion(motivo: "Roto"), "Pedro"));
            _ = await Assert.ThrowsExceptionAsync<ArgumentException>(() => servicio.Apuntar("1", "PICK", 100070, Peticion(motivo: "Otro"), "Pedro"));
            _ = await Assert.ThrowsExceptionAsync<ArgumentException>(() => servicio.Apuntar("1", "PICK", 100070, Peticion("99999"), "Pedro"));
            _ = await Assert.ThrowsExceptionAsync<ArgumentException>(() => servicio.Apuntar("1", "PACK", 100070, Peticion(), "Pedro"));
            A.CallTo(avisador).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task SinEtiqueta_RecogidaQueNoExiste_Null()
        {
            var (servicio, _, _) = ServicioSinEtiqueta(null);
            Assert.IsNull(await servicio.Apuntar("1", "PICK", 100070, Peticion(), "Pedro"));
        }

        [TestMethod]
        public async Task Avisador_SoloAAlmacenYNuncaACompras()
        {
            IServicioNotificacionesPush push = A.Fake<IServicioNotificacionesPush>();
            var grupos = new List<string>();
            var avisador = new AvisadorMuestraSinEtiqueta(push, g => { grupos.Add(g); return new List<string> { "Santi" }; },
                (e, a) => Task.FromResult(new List<string>()));

            Assert.IsTrue(await avisador.Avisar("1", "ALG", "45929", "MUESTRA X", "010/002/003", "NUEVAVISION\\Pedro", "la etiqueta no se lee", "PICK", 100070));

            CollectionAssert.AreEqual(new[] { Constantes.GruposSeguridad.ALMACEN }, grupos);
            A.CallTo(() => push.GuardarEnBuzonDeUsuario("NUEVAVISION\\Santi", Constantes.Aplicaciones.NESTO,
                A<NotificacionPushDTO>.That.Matches(n => n.Tipo == "MuestraSinEtiquetaEnHueco" && n.Datos["producto"] == "45929"
                    && n.Datos["hueco"] == "010/002/003" && n.Datos["almacen"] == "ALG" && n.Datos["usuario"] == "NUEVAVISION\\Pedro")))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Avisador_Cuerpo()
        {
            Assert.AreEqual("Pedro no ha podido leer la etiqueta de la muestra 45929 (MUESTRA X) en el hueco 010/002/003 de Algete (la etiqueta no se lee) "
                + "en el picking 100070 y la ha contado a mano. Reimprima la etiqueta y péguela.",
                AvisadorMuestraSinEtiqueta.Cuerpo("ALG", "45929", "MUESTRA X", "010/002/003", "NUEVAVISION\\Pedro", "la etiqueta no se lee", "PICK", 100070));
        }
    }
}
