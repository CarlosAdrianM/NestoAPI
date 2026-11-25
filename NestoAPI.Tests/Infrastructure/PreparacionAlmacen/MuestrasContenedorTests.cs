using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Infraestructure.Productos;
using NestoAPI.Infraestructure.Reposiciones;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using NestoAPI.Tests.Infrastructure.Productos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#884 (Carlos 24/11/26): las muestras (MMP) sin código de fabricante llevan la etiqueta en el cajón (REI, ALC) o la
    /// gaveta (ALG), con el interno del 29: (a) código en bloque, (b) etiquetas de cajón por lotes, (c) PideCantidad en las
    /// lecturas y (d) la hoja de muestras. Y una muestra con interno se sigue pudiendo teclear (SinCodigoAdmitido).
    /// </summary>
    [TestClass]
    public class MuestrasContenedorTests
    {
        private const string EMPRESA = "1";
        // EAN-13 con el dígito de control bien
        private const string FABRICANTE = "4006381333931";
        private static readonly string INTERNO_17404 = CodigoBarrasInterno.DeReferencia("17404");

        private static MuestraAlmacen Muestra(string producto, string nombre = null, string familia = "LISAP", string nombreFamilia = "Lisap",
            int stock = 0, short? estado = 0, string subGrupo = "MMP", params string[] codigos)
        {
            return new MuestraAlmacen
            {
                Producto = producto,
                Nombre = nombre ?? "MUESTRA " + producto,
                Familia = familia,
                NombreFamilia = nombreFamilia,
                SubGrupo = subGrupo,
                Estado = estado,
                Stock = stock,
                Codigos = codigos.Select(c => new CodigoBarrasProductoDTO { Codigo = c, Cantidad = 1, Activo = true }).ToList()
            };
        }

        private static MuestraAlmacen ConHuecos(MuestraAlmacen muestra, params string[] huecos)
        {
            muestra.Huecos = huecos.Select(h => new HuecoMuestra
            {
                Producto = muestra.Producto,
                Pasillo = h.Substring(0, 3),
                Fila = h.Substring(3, 3),
                Columna = h.Substring(6, 3),
                Cantidad = 2
            }).ToList();
            return muestra;
        }

        // ---------------------------------------------------------------- (a) qué muestras entran en el bloque

        [TestMethod]
        public void EntraEnBloque_SoloMuestrasActivasSinNingunCodigo()
        {
            Assert.IsTrue(ReglasMuestras.EntraEnBloque(Muestra("1")));
            Assert.IsTrue(ReglasMuestras.EntraEnBloque(Muestra("2", codigos: "1")), "Un código que no es GTIN no cuenta (#674)");
            Assert.IsFalse(ReglasMuestras.EntraEnBloque(Muestra("3", codigos: FABRICANTE)), "Con código del fabricante no");
            Assert.IsFalse(ReglasMuestras.EntraEnBloque(Muestra("17404", codigos: INTERNO_17404)), "Con el interno ya no entra: idempotente");
            Assert.IsFalse(ReglasMuestras.EntraEnBloque(Muestra("4", estado: -1)), "De baja no");
            Assert.IsFalse(ReglasMuestras.EntraEnBloque(Muestra("5", estado: null)), "Sin estado no (la consulta pide Estado >= 0)");
            Assert.IsFalse(ReglasMuestras.EntraEnBloque(Muestra("6", subGrupo: "ACP")), "Lo que no es muestra no");
            Assert.IsTrue(ReglasMuestras.EntraEnBloque(Muestra("7", subGrupo: "mmp ")), "El char con relleno y minúsculas");
        }

        [TestMethod]
        public void EntraEnBloque_UnCodigoDeBaja_NoCuenta()
        {
            MuestraAlmacen muestra = Muestra("8");
            muestra.Codigos.Add(new CodigoBarrasProductoDTO { Codigo = FABRICANTE, Cantidad = 1, Activo = false });

            Assert.IsTrue(ReglasMuestras.EntraEnBloque(muestra));
        }

        [TestMethod]
        public void ParaElBloque_PorReferenciaYSinRepetir()
        {
            List<MuestraAlmacen> bloque = ReglasMuestras.ParaElBloque(new[] { Muestra("300"), Muestra("100"), Muestra("100"), Muestra("200", codigos: FABRICANTE) });

            CollectionAssert.AreEqual(new[] { "100", "300" }, bloque.Select(m => m.Producto).ToList());
        }

        // ---------------------------------------------------------------- (b) filtros y orden de las etiquetas

        [TestMethod]
        public void Filtrar_FamiliaPorCodigoONombre_ConStock_YLista()
        {
            var muestras = new[]
            {
                Muestra("1", familia: "LISAP", nombreFamilia: "Lisap", stock: 3),
                Muestra("2", familia: "FABY", nombreFamilia: "Faby", stock: 0),
                Muestra("3", familia: "LISAP", nombreFamilia: "Lisap", stock: 0),
                Muestra("4", familia: "LISAP", nombreFamilia: "Lisap", stock: 5, estado: -1)
            };

            CollectionAssert.AreEqual(new[] { "1", "3" }, ReglasMuestras.Filtrar(muestras, " lisap ", false, null).Select(m => m.Producto).ToList());
            CollectionAssert.AreEqual(new[] { "2" }, ReglasMuestras.Filtrar(muestras, "Faby", false, null).Select(m => m.Producto).ToList(), "Por el nombre");
            CollectionAssert.AreEqual(new[] { "1" }, ReglasMuestras.Filtrar(muestras, null, true, null).Select(m => m.Producto).ToList());
            CollectionAssert.AreEqual(new[] { "3" }, ReglasMuestras.Filtrar(muestras, null, false, new[] { " 3 ", "4", "99" }).Select(m => m.Producto).ToList());
            CollectionAssert.AreEqual(new[] { "4", "99" }, ReglasMuestras.NoSonMuestras(muestras, new[] { "3", "4", "99" }));
        }

        [TestMethod]
        public void Etiquetas_Tienda_PorFamiliaYNombre_SinFamiliaAlFinal()
        {
            List<EtiquetaMuestraDTO> etiquetas = ReglasMuestras.Etiquetas(new[]
            {
                Muestra("1", "ÚLTIMA DE LISAP", "LISAP", "Lisap"),
                Muestra("2", "SIN MARCA", null, null),
                Muestra("3", "ACEITE", "LISAP", "Lisap"),
                Muestra("4", "CREMA", "FABY", "Faby")
            }, null);

            CollectionAssert.AreEqual(new[] { "4", "3", "1", "2" }, etiquetas.Select(e => e.Producto).ToList());
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, etiquetas.Select(e => e.Orden).ToList());
            Assert.IsTrue(etiquetas.All(e => e.Hueco == null));
        }

        [TestMethod]
        public void Etiquetas_Algete_UnaPorHuecoPorElRecorrido_YLasSinHuecoDetras()
        {
            List<EtiquetaMuestraDTO> etiquetas = ReglasMuestras.Etiquetas(new[]
            {
                ConHuecos(Muestra("1", "AAA"), "002001001"),
                ConHuecos(Muestra("2", "BBB"), "001002001", "003001001"),
                Muestra("3", "SIN HUECO"),
                ConHuecos(Muestra("4", "CCC"), "001001002")
            }, OrdenRecorrido.ComoHoy);

            // Sin calles: pasillo, columna y fila
            CollectionAssert.AreEqual(new[] { "2", "4", "1", "2", "3" }, etiquetas.Select(e => e.Producto).ToList());
            CollectionAssert.AreEqual(new[] { "001/002/001", "001/001/002", "002/001/001", "003/001/001", null }, etiquetas.Select(e => e.Hueco).ToList());
        }

        [TestMethod]
        public void Etiquetas_ElCodigoDelFabricanteAntesQueElInterno_YSinCodigoNull()
        {
            List<EtiquetaMuestraDTO> etiquetas = ReglasMuestras.Etiquetas(new[]
            {
                Muestra("17404", "A", codigos: new[] { INTERNO_17404, FABRICANTE }),
                Muestra("2", "B", codigos: CodigoBarrasInterno.DeReferencia("2")),
                Muestra("3", "C")
            }, null);

            Assert.AreEqual(FABRICANTE, etiquetas[0].CodigoBarras);
            Assert.IsFalse(etiquetas[0].CodigoInterno);
            Assert.AreEqual(CodigoBarrasInterno.DeReferencia("2"), etiquetas[1].CodigoBarras);
            Assert.IsTrue(etiquetas[1].CodigoInterno);
            Assert.IsNull(etiquetas[2].CodigoBarras);
        }

        // ---------------------------------------------------------------- la etiqueta de contenedor

        [TestMethod]
        public void ComponerContenedor_CabeceraMuestraYNombreEnGrandeSinLaPalabraMuestra()
        {
            EtiquetaContenedorMuestra etiqueta = GeneradorEtiquetasProducto.ComponerContenedor("17404", "MUESTRA CHAMPU KERATINA LISAP 10ML", INTERNO_17404);

            Assert.AreEqual("MUESTRA · Ref. 17404", etiqueta.Cabecera);
            CollectionAssert.AreEqual(new[] { "CHAMPU KERATINA", "LISAP 10ML" }, etiqueta.Nombre);
            Assert.AreEqual(INTERNO_17404, etiqueta.CodigoBarras);
            CollectionAssert.AreEqual(new[] { "MUESTRA · Ref. 17404", "CHAMPU KERATINA", "LISAP 10ML" }, etiqueta.Lineas);
        }

        [TestMethod]
        public void ComponerContenedor_NombreLargo_DosLineasComoMucho_YSoloMuestraSeQueda()
        {
            Assert.AreEqual(GeneradorEtiquetasProducto.CONTENEDOR_NOMBRE_LINEAS,
                GeneradorEtiquetasProducto.ComponerContenedor("1", "UNO DOS TRES CUATRO CINCO SEIS SIETE OCHO NUEVE DIEZ", FABRICANTE).Nombre.Count);
            CollectionAssert.AreEqual(new[] { "MUESTRA" }, GeneradorEtiquetasProducto.ComponerContenedor("1", "MUESTRA", FABRICANTE).Nombre);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void ComponerContenedor_SinEan13Valido_Lanza()
        {
            _ = GeneradorEtiquetasProducto.ComponerContenedor("1", "X", "1234");
        }

        [TestMethod]
        public void TrabajoContenedores_UnSoloTrabajoConUnaEtiquetaPorContenedor()
        {
            var etiquetas = new List<EtiquetaContenedorMuestra>
            {
                GeneradorEtiquetasProducto.ComponerContenedor("17404", "MUESTRA A", INTERNO_17404),
                GeneradorEtiquetasProducto.ComponerContenedor("2", "MUESTRA B", FABRICANTE)
            };

            TrabajoEtiquetas trabajo = GeneradorEtiquetasProducto.TrabajoContenedores(etiquetas);

            Assert.AreEqual(1, Veces(trabajo.Epl, "I8,A,034"), "La cabecera del trabajo, una vez");
            Assert.AreEqual(2, Veces(trabajo.Epl, "\r\nP1\r\n"));
            StringAssert.Contains(trabajo.Epl, ",E30,");
            StringAssert.Contains(trabajo.Epl, "\"" + INTERNO_17404.Substring(0, 12) + "\"");
            StringAssert.Contains(trabajo.Epl, "MUESTRA");
            Assert.AreEqual(2, Veces(trabajo.Zpl, "^XA"));
            StringAssert.Contains(trabajo.Zpl, "^BEN," + GeneradorEtiquetasProducto.CONTENEDOR_EAN_ALTO);
            StringAssert.Contains(trabajo.Zpl, "^FD" + FABRICANTE.Substring(0, 12) + "^FS");
        }

        [TestMethod]
        public void Contenedor_CabeEnLaEtiqueta()
        {
            int fin = GeneradorEtiquetasProducto.CONTENEDOR_EAN_Y + GeneradorEtiquetasProducto.CONTENEDOR_EAN_ALTO + GeneradorEtiquetasProducto.EAN_CIFRAS_ALTO;
            Assert.IsTrue(fin <= GeneradorEtiquetasHueco.ALTO, $"Acaba en {fin} de {GeneradorEtiquetasHueco.ALTO}");
            Assert.IsTrue(GeneradorEtiquetasProducto.CONTENEDOR_NOMBRE_Y + GeneradorEtiquetasProducto.CONTENEDOR_NOMBRE_LINEAS
                * GeneradorEtiquetasProducto.CONTENEDOR_NOMBRE_INTERLINEA <= GeneradorEtiquetasProducto.CONTENEDOR_EAN_Y);
        }

        private static int Veces(string texto, string trozo)
        {
            int n = 0;
            for (int i = texto.IndexOf(trozo, StringComparison.Ordinal); i >= 0; i = texto.IndexOf(trozo, i + trozo.Length, StringComparison.Ordinal))
            {
                n++;
            }
            return n;
        }

        // ---------------------------------------------------------------- EAN-13 para el PDF

        [TestMethod]
        public void Ean13_GuardasYParidadDeLaPrimeraCifra()
        {
            const string codigo = "5901234123457"; // el 5 = LGGLLG
            bool[] modulos = Ean13.Modulos(codigo);

            Assert.AreEqual(95, modulos.Length);
            Assert.AreEqual("101", Bits(modulos, 0, 3));
            Assert.AreEqual("01010", Bits(modulos, 45, 5));
            Assert.AreEqual("101", Bits(modulos, 92, 3));
            var paridad = new StringBuilder();
            for (int i = 0; i < 6; i++)
            {
                int unos = Bits(modulos, 3 + i * 7, 7).Count(c => c == '1');
                paridad.Append(unos % 2 == 1 ? 'L' : 'G');
            }
            Assert.AreEqual("LGGLLG", paridad.ToString());
            // La derecha (juego R) empieza por barra y tiene paridad par
            for (int i = 0; i < 6; i++)
            {
                string r = Bits(modulos, 50 + i * 7, 7);
                Assert.AreEqual('1', r[0]);
                Assert.AreEqual(0, r.Count(c => c == '1') % 2);
            }
            Assert.AreEqual(95, Ean13.Tramos(codigo).Sum(t => t.Modulos));
        }

        [TestMethod]
        public void Ean13_PrimeraCifraCero_TodoL_YElCeroEsSuPatron()
        {
            bool[] modulos = Ean13.Modulos("0012345678905"); // el UPC-A 012345678905 como EAN-13

            Assert.AreEqual("0001101", Bits(modulos, 3, 7), "La segunda cifra (0) en el juego L");
            Assert.AreEqual("0011001", Bits(modulos, 10, 7), "La tercera (1), también L: la primera cifra 0 = LLLLLL");
            Assert.AreEqual("1010000", Bits(modulos, 50, 7), "La octava (6) en el juego R");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Ean13_ControlMal_Lanza()
        {
            _ = Ean13.Modulos("5901234123458");
        }

        private static string Bits(bool[] modulos, int desde, int cuantos)
            => new string(modulos.Skip(desde).Take(cuantos).Select(b => b ? '1' : '0').ToArray());

        // ---------------------------------------------------------------- (a) el servicio del bloque

        private IRepositorioMuestras repositorio;
        private RepositorioCodigosBarrasFalso codigosFalsos;
        private ILectorParametrosUsuario parametros;
        private IImpresoraEtiquetasAlmacen impresora;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = A.Fake<IRepositorioMuestras>();
            codigosFalsos = new RepositorioCodigosBarrasFalso();
            parametros = A.Fake<ILectorParametrosUsuario>();
            impresora = A.Fake<IImpresoraEtiquetasAlmacen>();
            A.CallTo(() => parametros.LeerParametro(EMPRESA, "Paloma", "ImpresoraCodBarras")).Returns(@"\\RDS2016\etiquetasreina");
        }

        private void Muestras(params MuestraAlmacen[] muestras)
        {
            A.CallTo(() => repositorio.LeerMuestras(EMPRESA, A<string>._)).Returns(muestras.ToList());
            foreach (MuestraAlmacen m in muestras)
            {
                _ = codigosFalsos.Producto(m.Producto, null, m.Nombre);
                foreach (CodigoBarrasProductoDTO c in m.Codigos)
                {
                    _ = codigosFalsos.Fila(m.Producto, c.Codigo);
                }
            }
        }

        [TestMethod]
        public async Task Bloque_Ensayo_NoEscribeNada_YDiceElCodigoQueLeTocaria()
        {
            Muestras(Muestra("17404"), Muestra("MF0581"), Muestra("3", codigos: FABRICANTE));
            var servicio = new ServicioCodigoInternoMuestras(repositorio, new ServicioCodigosBarras(codigosFalsos));

            ResultadoCodigoInternoMuestrasDTO resultado = await servicio.Dar(EMPRESA, @"NUEVAVISION\Carlos", true);

            Assert.IsTrue(resultado.Ensayo);
            Assert.AreEqual(2, resultado.Total);
            Assert.AreEqual(0, resultado.Asignados);
            Assert.AreEqual(INTERNO_17404, resultado.Muestras.Single(m => m.Producto == "17404").Codigo);
            Assert.IsNull(resultado.Muestras.Single(m => m.Producto == "MF0581").Codigo, "Le tocaría uno del contador");
            Assert.AreEqual(0, codigosFalsos.InternosPedidos);
            A.CallTo(() => repositorio.ApuntarRastro(A<string>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Bloque_DeVerdad_PorElCaminoDeSiempre_YUnaFilaDeRastro()
        {
            Muestras(Muestra("17404"), Muestra("MF0581"), Muestra("3", codigos: FABRICANTE));
            var servicio = new ServicioCodigoInternoMuestras(repositorio, new ServicioCodigosBarras(codigosFalsos));

            ResultadoCodigoInternoMuestrasDTO resultado = await servicio.Dar(EMPRESA, @"NUEVAVISION\Carlos", false);

            Assert.AreEqual(2, resultado.Asignados);
            Assert.AreEqual(0, resultado.Errores);
            Assert.AreEqual(2, codigosFalsos.InternosPedidos, "Cada una por GuardarInternoEnExclusiva");
            Assert.AreEqual(INTERNO_17404, resultado.Muestras.Single(m => m.Producto == "17404").Codigo);
            StringAssert.StartsWith(resultado.Muestras.Single(m => m.Producto == "MF0581").Codigo, CodigoBarrasInterno.PREFIJO_CONTADOR);
            A.CallTo(() => repositorio.ApuntarRastro(ServicioCodigoInternoMuestras.TABLA_RASTRO, null,
                A<string>.That.Contains("17404"), @"NUEVAVISION\Carlos")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Bloque_UnaQueFalla_NoParaLasDemas()
        {
            Muestras(Muestra("1"), Muestra("2"));
            IServicioCodigosBarras codigos = A.Fake<IServicioCodigosBarras>();
            A.CallTo(() => codigos.AsignarInterno(EMPRESA, "1", A<string>._, false)).Throws(new InvalidOperationException("bloqueo"));
            A.CallTo(() => codigos.AsignarInterno(EMPRESA, "2", A<string>._, false)).Returns(new ResultadoCodigoBarras
            {
                Estado = EstadoOperacionCodigoBarras.Creado,
                Codigo = new CodigoBarrasProductoDTO { Codigo = CodigoBarrasInterno.DeReferencia("2") }
            });
            var servicio = new ServicioCodigoInternoMuestras(repositorio, codigos);

            ResultadoCodigoInternoMuestrasDTO resultado = await servicio.Dar(EMPRESA, "Carlos", false);

            Assert.AreEqual(1, resultado.Asignados);
            Assert.AreEqual(1, resultado.Errores);
            Assert.AreEqual("bloqueo", resultado.Muestras.Single(m => m.Producto == "1").Error);
        }

        [TestMethod]
        public async Task Bloque_SinNingunaPorHacer_NoLlamaANada()
        {
            Muestras(Muestra("3", codigos: FABRICANTE), Muestra("17404", codigos: INTERNO_17404));
            var servicio = new ServicioCodigoInternoMuestras(repositorio, new ServicioCodigosBarras(codigosFalsos));

            ResultadoCodigoInternoMuestrasDTO resultado = await servicio.Dar(EMPRESA, "Carlos", false);

            Assert.AreEqual(0, resultado.Total);
            Assert.AreEqual(0, codigosFalsos.InternosPedidos);
            StringAssert.Contains(resultado.Mensaje, "no hay que dar ninguno");
        }

        // ---------------------------------------------------------------- (b) el servicio de las etiquetas

        private ServicioEtiquetasMuestras ServicioEtiquetas()
            => new ServicioEtiquetasMuestras(repositorio, null, parametros, impresora, new ServicioCodigosBarras(codigosFalsos));

        [TestMethod]
        public async Task Etiquetas_Ensayo_NiImprimeNiDaCodigos()
        {
            Muestras(Muestra("17404"), Muestra("3", codigos: FABRICANTE));

            ResultadoEtiquetasMuestrasDTO resultado = await ServicioEtiquetas().Imprimir(EMPRESA,
                new PeticionEtiquetasMuestrasDTO { Almacen = "rei" }, @"NUEVAVISION\Paloma", true);

            Assert.AreEqual("REI", resultado.Almacen);
            Assert.AreEqual(2, resultado.Total);
            Assert.AreEqual(0, resultado.Impresas);
            Assert.AreEqual(1, resultado.SinCodigo);
            Assert.AreEqual(@"\\RDS2016\etiquetasreina", resultado.Impresora);
            Assert.IsTrue(resultado.Etiquetas.All(e => e.Lineas.Count >= 2), "El ensayo enseña el texto");
            Assert.AreEqual(0, codigosFalsos.InternosPedidos);
            A.CallTo(() => impresora.Imprimir(A<string>._, A<TrabajoEtiquetas>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Etiquetas_Ensayo_SinImpresoraPedida_LaDelAlmacen_NoLaDelUsuario()
        {
            // Caso de Carlos (25/11/26): usuario de Algete en la tienda de Alcobendas, las muestras de ALC
            Muestras(Muestra("3", codigos: FABRICANTE));
            A.CallTo(() => parametros.LeerParametro(EMPRESA, "Carlos", "ImpresoraCodBarras")).Returns(@"\\RDS2016\etiquetas2");

            ResultadoEtiquetasMuestrasDTO resultado = await ServicioEtiquetas().Imprimir(EMPRESA,
                new PeticionEtiquetasMuestrasDTO { Almacen = "ALC" }, @"NUEVAVISION\Carlos", true);

            Assert.AreEqual(@"\\RDS2016\etiquetasalcobendas", resultado.Impresora);
            Assert.AreEqual(OrigenesImpresoraEtiquetas.ALMACEN, resultado.OrigenImpresora);
            StringAssert.StartsWith(resultado.Mensaje, @"Saldrían 1 etiqueta de muestras de ALC en \\RDS2016\etiquetasalcobendas");
        }

        [TestMethod]
        public async Task Etiquetas_ConFilaGeneralDelAlmacen_Esa()
        {
            Muestras(Muestra("3", codigos: FABRICANTE));
            A.CallTo(() => parametros.LeerParametroSinDefecto("1", "(defecto)", "ImpresoraEtiquetas.ALC")).Returns(@"\\RDS2016\zebraalc");

            ResultadoEtiquetasMuestrasDTO resultado = await ServicioEtiquetas().Imprimir(EMPRESA,
                new PeticionEtiquetasMuestrasDTO { Almacen = "ALC" }, @"NUEVAVISION\Otro", true);

            Assert.AreEqual(@"\\RDS2016\zebraalc", resultado.Impresora);
        }

        [TestMethod]
        public async Task Etiquetas_ConImpresoraPedida_Esa()
        {
            Muestras(Muestra("3", codigos: FABRICANTE));

            ResultadoEtiquetasMuestrasDTO resultado = await ServicioEtiquetas().Imprimir(EMPRESA,
                new PeticionEtiquetasMuestrasDTO { Almacen = "ALC", Impresora = @"\\RDS2016\etiquetasreina" }, @"NUEVAVISION\Otro", false);

            Assert.AreEqual(@"\\RDS2016\etiquetasreina", resultado.Impresora);
            Assert.AreEqual(OrigenesImpresoraEtiquetas.PEDIDA, resultado.OrigenImpresora);
            A.CallTo(() => impresora.Imprimir(@"\\RDS2016\etiquetasreina", A<TrabajoEtiquetas>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Etiquetas_ImpresoraPedidaQueNoVale_LanzaTambienEnElEnsayo()
        {
            Muestras(Muestra("17404"));

            _ = await Assert.ThrowsExceptionAsync<ArgumentException>(() => ServicioEtiquetas().Imprimir(EMPRESA,
                new PeticionEtiquetasMuestrasDTO { Almacen = "ALC", Impresora = @"\\maquina-de-fuera\cola" }, "Paloma", true));
            _ = await Assert.ThrowsExceptionAsync<ArgumentException>(() => ServicioEtiquetas().Imprimir(EMPRESA,
                new PeticionEtiquetasMuestrasDTO { Almacen = "ALC", Impresora = @"\\RDS2016\cola;rm" }, "Paloma", false));

            Assert.AreEqual(0, codigosFalsos.InternosPedidos);
            A.CallTo(() => impresora.Imprimir(A<string>._, A<TrabajoEtiquetas>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Etiquetas_DeVerdad_DaElInternoALaQueNoTiene_YUnSoloTrabajo()
        {
            Muestras(Muestra("17404"), Muestra("3", codigos: FABRICANTE));
            TrabajoEtiquetas mandado = null;
            A.CallTo(() => impresora.Imprimir(A<string>._, A<TrabajoEtiquetas>._))
                .Invokes((string _, TrabajoEtiquetas t) => mandado = t).Returns(LenguajeEtiquetas.Epl);

            ResultadoEtiquetasMuestrasDTO resultado = await ServicioEtiquetas().Imprimir(EMPRESA,
                new PeticionEtiquetasMuestrasDTO { Almacen = "REI" }, @"NUEVAVISION\Paloma", false);

            Assert.AreEqual(2, resultado.Impresas);
            Assert.AreEqual(1, resultado.CodigosAsignados);
            Assert.AreEqual(1, codigosFalsos.InternosPedidos);
            EtiquetaMuestraDTO dada = resultado.Etiquetas.Single(e => e.Producto == "17404");
            Assert.AreEqual(INTERNO_17404, dada.CodigoBarras);
            Assert.IsTrue(dada.CodigoAsignado);
            A.CallTo(() => impresora.Imprimir(@"\\RDS2016\etiquetasreina", A<TrabajoEtiquetas>._)).MustHaveHappenedOnceExactly();
            Assert.AreEqual(2, Veces(mandado.Epl, "\r\nP1\r\n"));
        }

        [TestMethod]
        public async Task Etiquetas_DeVerdad_UsuarioSinImpresora_SaleEnLaDelAlmacen()
        {
            Muestras(Muestra("3", codigos: FABRICANTE));

            ResultadoEtiquetasMuestrasDTO resultado = await ServicioEtiquetas().Imprimir(EMPRESA,
                new PeticionEtiquetasMuestrasDTO { Almacen = "REI" }, @"NUEVAVISION\Otro", false);

            Assert.AreEqual(1, resultado.Impresas);
            A.CallTo(() => impresora.Imprimir(@"\\RDS2016\etiquetasreina", A<TrabajoEtiquetas>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Etiquetas_AlmacenQueNoVale_Lanza()
        {
            ArgumentException ex = await Assert.ThrowsExceptionAsync<ArgumentException>(() => ServicioEtiquetas().Imprimir(EMPRESA,
                new PeticionEtiquetasMuestrasDTO { Almacen = "XXX" }, "Paloma", true));
            StringAssert.Contains(ex.Message, "ALG");
            _ = await Assert.ThrowsExceptionAsync<ArgumentException>(() => ServicioEtiquetas().Imprimir(EMPRESA,
                new PeticionEtiquetasMuestrasDTO(), "Paloma", true));
        }

        [TestMethod]
        public async Task Etiquetas_DeMasDe500_Lanza()
        {
            Muestras(Enumerable.Range(1, ReglasMuestras.MAXIMO_ETIQUETAS + 1).Select(i => Muestra(i.ToString(), codigos: FABRICANTE)).ToArray());

            _ = await Assert.ThrowsExceptionAsync<ArgumentException>(() => ServicioEtiquetas().Imprimir(EMPRESA,
                new PeticionEtiquetasMuestrasDTO { Almacen = "ALG" }, "Paloma", true));
        }

        [TestMethod]
        public async Task Etiquetas_Algete_PideElRecorridoDelAlmacen()
        {
            Muestras(ConHuecos(Muestra("1", codigos: FABRICANTE), "001001001"));
            ILectorOrdenRecorrido recorrido = A.Fake<ILectorOrdenRecorrido>();
            A.CallTo(() => recorrido.DelAlmacen(EMPRESA, "ALG")).Returns(OrdenRecorrido.ComoHoy);
            var servicio = new ServicioEtiquetasMuestras(repositorio, recorrido, parametros, impresora, new ServicioCodigosBarras(codigosFalsos));

            ResultadoEtiquetasMuestrasDTO resultado = await servicio.Imprimir(EMPRESA, new PeticionEtiquetasMuestrasDTO { Almacen = "ALG" }, "Paloma", true);

            Assert.AreEqual("001/001/001", resultado.Etiquetas.Single().Hueco);
            A.CallTo(() => recorrido.DelAlmacen(EMPRESA, "ALG")).MustHaveHappenedOnceExactly();
            A.CallTo(() => repositorio.LeerMuestras(EMPRESA, "ALG")).MustHaveHappenedOnceExactly();
        }

        // ---------------------------------------------------------------- (c) una muestra con interno se sigue tecleando, y PideCantidad

        [TestMethod]
        public void OrdenarRecorrido_MuestraCuyoUnicoCodigoEsElInterno_SigueAdmitidaYPideCantidad()
        {
            List<LineaPickingAlmacenDTO> lineas = CasadorEscaneos.OrdenarRecorrido(new[]
            {
                // El interno nunca va en la ficha (CodigoBarras): solo en CodigosBarras
                new LineaPickingAlmacenDTO { Producto = "17404", SubGrupoProducto = "MMP", CodigoBarras = null, CodigosBarras = new List<string> { INTERNO_17404 }, Cantidad = 1 },
                new LineaPickingAlmacenDTO { Producto = "M2", SubGrupoProducto = "MMP", CodigoBarras = FABRICANTE, Cantidad = 1 },
                new LineaPickingAlmacenDTO { Producto = "P1", SubGrupoProducto = "ACP", CodigoBarras = FABRICANTE, Cantidad = 1 }
            });

            LineaPickingAlmacenDTO conInterno = lineas.Single(l => l.Producto == "17404");
            Assert.IsTrue(conInterno.SinCodigoAdmitido, "Decisión de Carlos 24/11/26: se puede seguir tecleando sin aviso");
            Assert.IsTrue(conInterno.PideCantidad);
            Assert.IsTrue(lineas.Single(l => l.Producto == "M2").PideCantidad, "Una muestra con código del fabricante también pide la cantidad");
            Assert.IsFalse(lineas.Single(l => l.Producto == "M2").SinCodigoAdmitido);
            Assert.IsFalse(lineas.Single(l => l.Producto == "P1").PideCantidad, "Lo que no es muestra, como siempre");
        }

        [TestMethod]
        public void RepartirLoResuelto_CopiaPideCantidad()
        {
            List<LineaPickingAlmacenDTO> recorrido = CasadorEscaneos.OrdenarRecorrido(new[]
            {
                new LineaPickingAlmacenDTO { Producto = "M1", SubGrupoProducto = "MMP", Cantidad = 2 }
            });

            List<LineaRecogidaDTO> lineas = CasadorEscaneos.RepartirLoResuelto(recorrido, new List<CasadorEscaneos.Cantidad>());

            Assert.IsTrue(lineas.Single().PideCantidad);
        }

        [TestMethod]
        public void PideCantidad_EsLaMuestra()
        {
            Assert.IsTrue(MuestrasSinCodigo.PideCantidad("MMP"));
            Assert.IsTrue(MuestrasSinCodigo.PideCantidad("mmp "));
            Assert.IsFalse(MuestrasSinCodigo.PideCantidad("ACP"));
            Assert.IsFalse(MuestrasSinCodigo.PideCantidad(null));
        }

        [TestMethod]
        public async Task BuscarPorCodigo_UnaMuestra_PideCantidad()
        {
            IRepositorioCodigosBarras repo = A.Fake<IRepositorioCodigosBarras>();
            A.CallTo(() => repo.ProductosConCodigo(EMPRESA, INTERNO_17404)).Returns(new List<ProductoPorCodigoBarrasDTO>
            {
                new ProductoPorCodigoBarrasDTO { Producto = "17404", Nombre = "MUESTRA", Cantidad = 1, SubGrupo = "MMP " }
            });
            A.CallTo(() => repo.ProductosConCodigo(EMPRESA, FABRICANTE)).Returns(new List<ProductoPorCodigoBarrasDTO>
            {
                new ProductoPorCodigoBarrasDTO { Producto = "1", Nombre = "CHAMPU", Cantidad = 1, Principal = true, SubGrupo = "ACP" }
            });
            var servicio = new ServicioCodigosBarras(repo);

            Assert.IsTrue((await servicio.BuscarPorCodigo(EMPRESA, INTERNO_17404)).Single().PideCantidad);
            Assert.IsFalse((await servicio.BuscarPorCodigo(EMPRESA, FABRICANTE)).Single().PideCantidad);
        }

        [TestMethod]
        public void Inventario_PideCantidadPorElSubgrupo()
        {
            Assert.IsTrue(new Inventario { Subgrupo = "MMP" }.PideCantidad);
            Assert.IsFalse(new Inventario { Subgrupo = "ACP" }.PideCantidad);
            Assert.IsFalse(new Inventario().PideCantidad);
        }

        [TestMethod]
        public async Task ReposicionPuntual_UnaMuestra_PideCantidad()
        {
            var puntual = new ProductosReposicionPuntual(
                (empresa, origen, producto) => Task.FromResult(producto == "17404"
                    ? new ProductoReposicionPuntualDTO { Producto = "17404", SubGrupo = "MMP" }
                    : null),
                (empresa, codigo) => Task.FromResult(new List<ProductoPorCodigoBarrasDTO>
                {
                    new ProductoPorCodigoBarrasDTO { Producto = "17404", Cantidad = 1, PideCantidad = true }
                }));

            Assert.IsTrue((await puntual.Buscar(EMPRESA, "REI", "17404")).Single().PideCantidad, "Tecleada la referencia");
            Assert.IsTrue((await puntual.Buscar(EMPRESA, "REI", INTERNO_17404)).Single().PideCantidad, "Leída la etiqueta");
        }

        // ---------------------------------------------------------------- (d) la hoja de muestras

        [TestMethod]
        public async Task Hoja_PorFamiliaYNombre_ConElCodigoYLaFotoQueLlega()
        {
            Muestras(Muestra("1", "ZETA", "LISAP", "Lisap", codigos: FABRICANTE), Muestra("2", "ALFA", "LISAP", "Lisap"),
                Muestra("3", "BETA", "FABY", "Faby"), Muestra("4", "OTRA", "FABY", "Faby", estado: -1));
            IFotosProductoAlmacen fotos = A.Fake<IFotosProductoAlmacen>();
            A.CallTo(() => fotos.Urls(A<IEnumerable<string>>._)).Returns(new Dictionary<string, string> { ["1"] = "http://foto/1.jpg", ["2"] = "http://foto/mal.jpg" });
            var servicio = new ServicioHojaMuestras(repositorio, fotos,
                url => url.Contains("mal") ? throw new InvalidOperationException("no llega") : Task.FromResult(new byte[] { 1, 2, 3 }),
                () => new DateTime(2026, 11, 24, 10, 0, 0));

            HojaMuestras hoja = await servicio.Preparar(EMPRESA, null, null, false, true);

            CollectionAssert.AreEqual(new[] { "3", "2", "1" }, hoja.Muestras.Select(m => m.Producto).ToList(), "Faby antes que Lisap; dentro, por nombre; sin las de baja");
            Assert.AreEqual(FABRICANTE, hoja.Muestras.Single(m => m.Producto == "1").CodigoBarras);
            Assert.IsNull(hoja.Muestras.Single(m => m.Producto == "2").CodigoBarras);
            Assert.IsNotNull(hoja.Muestras.Single(m => m.Producto == "1").Foto);
            Assert.IsNull(hoja.Muestras.Single(m => m.Producto == "2").Foto, "La que no llega, sin foto");
            A.CallTo(() => repositorio.LeerMuestras(EMPRESA, null)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Hoja_SinFotos_NoPreguntaPorEllas()
        {
            Muestras(Muestra("1"));
            IFotosProductoAlmacen fotos = A.Fake<IFotosProductoAlmacen>();
            var servicio = new ServicioHojaMuestras(repositorio, fotos, url => Task.FromResult(new byte[] { 1 }), () => DateTime.Now);

            _ = await servicio.Preparar(EMPRESA, "ALC", "Lisap", false, false);

            A.CallTo(() => fotos.Urls(A<IEnumerable<string>>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.LeerMuestras(EMPRESA, "ALC")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Hoja_SoloConStockSinAlmacen_O_AlmacenQueNoVale_Lanza()
        {
            var servicio = new ServicioHojaMuestras(repositorio, null);

            _ = await Assert.ThrowsExceptionAsync<ArgumentException>(() => servicio.Preparar(EMPRESA, null, null, true, false));
            _ = await Assert.ThrowsExceptionAsync<ArgumentException>(() => servicio.Preparar(EMPRESA, "XXX", null, false, false));
        }

        [TestMethod]
        public void Hoja_GeneraElPdf()
        {
            var hoja = new HojaMuestras
            {
                Almacen = "REI",
                Familia = "Lisap",
                Fecha = new DateTime(2026, 11, 24),
                Muestras = new List<FichaHojaMuestra>
                {
                    new FichaHojaMuestra { Producto = "17404", Nombre = "MUESTRA CHAMPU", Familia = "Lisap", CodigoBarras = INTERNO_17404 },
                    new FichaHojaMuestra { Producto = "2", Nombre = "MUESTRA SIN CODIGO", Familia = "Lisap" }
                }
            };

            byte[] pdf = GeneradorPdfHojaMuestras.Generar(hoja);

            Assert.IsTrue(pdf.Length > 1000);
            Assert.AreEqual("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        }
    }
}
