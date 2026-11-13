using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosCompra.PlazosEntrega;
using NestoAPI.Infraestructure.PedidosCompra.PlazosEntrega.FechasOrientativas;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PedidosCompra
{
    /// <summary>NestoAPI#861: apuntar y medir las fechas orientativas de lo no pedido (reglas puras y el servicio del job).</summary>
    [TestClass]
    public class FechasOrientativasTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 13); // viernes
        private static readonly DateTime LUNES = new DateTime(2026, 11, 16);

        private static bool FinDeSemana(DateTime d) => d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday;

        private static PlazoProveedor Generico => new PlazoProveedor { Empresa = "1", Proveedor = CalculoPlazosProveedores.PROVEEDOR_GENERICO, Recepciones = 2748, DiasP80 = 11 };

        private static PlazoProveedor Propio(DateTime ultimo, int cadencia = 14, int recepciones = 252, int p80 = 4) =>
            new PlazoProveedor { Empresa = "1", Proveedor = "65", Recepciones = recepciones, DiasP80 = p80, IntervaloPedidosMediano = cadencia, UltimoPedido = ultimo };

        private static PartidaFechaEntregaAgenciaDTO SinPedir(string producto, int unidades, DateTime salida) => new PartidaFechaEntregaAgenciaDTO
        {
            Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_SIN_STOCK,
            Producto = producto,
            Unidades = unidades,
            FechaOrientativa = salida
        };

        // ---------- Partidas y productos ----------

        [TestMethod]
        public void EsOrientativaSinPedir_SoloSinFechaSinStockYSinPedidoLejano()
        {
            Assert.IsTrue(ReglasFechasOrientativas.EsOrientativaSinPedir(SinPedir("17404", 1, HOY.AddDays(10))));

            PartidaFechaEntregaAgenciaDTO lejano = SinPedir("17404", 1, HOY.AddDays(30));
            lejano.FechaPrevistaProveedorOrientativo = HOY.AddDays(29);
            Assert.IsFalse(ReglasFechasOrientativas.EsOrientativaSinPedir(lejano), "la de un pedido enviado con la prevista lejana no es «sin pedir»");

            PartidaFechaEntregaAgenciaDTO sinOrientativa = SinPedir("17404", 1, HOY);
            sinOrientativa.FechaOrientativa = null;
            Assert.IsFalse(ReglasFechasOrientativas.EsOrientativaSinPedir(sinOrientativa));

            PartidaFechaEntregaAgenciaDTO conFecha = SinPedir("17404", 1, HOY);
            conFecha.PuedeSalirEl = HOY;
            Assert.IsFalse(ReglasFechasOrientativas.EsOrientativaSinPedir(conFecha));

            PartidaFechaEntregaAgenciaDTO algete = SinPedir("17404", 1, HOY);
            algete.Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_ALGETE;
            Assert.IsFalse(ReglasFechasOrientativas.EsOrientativaSinPedir(algete));
        }

        [TestMethod]
        public void ProductosSinPedir_UnoPorProductoConUnidadesPedidosYLaSalidaMasTemprana()
        {
            var calculos = new List<KeyValuePair<int, FechaEntregaAgenciaDTO>>
            {
                new KeyValuePair<int, FechaEntregaAgenciaDTO>(100, new FechaEntregaAgenciaDTO { Desglose = new List<PartidaFechaEntregaAgenciaDTO>
                {
                    SinPedir("17404 ", 2, HOY.AddDays(12)),
                    new PartidaFechaEntregaAgenciaDTO { Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_ALGETE, Producto = "999", Unidades = 1, PuedeSalirEl = HOY }
                } }),
                new KeyValuePair<int, FechaEntregaAgenciaDTO>(101, new FechaEntregaAgenciaDTO { Desglose = new List<PartidaFechaEntregaAgenciaDTO>
                {
                    SinPedir("17404", 3, HOY.AddDays(10))
                } }),
                new KeyValuePair<int, FechaEntregaAgenciaDTO>(102, null)
            };

            List<ProductoSinPedir> productos = ReglasFechasOrientativas.ProductosSinPedir(calculos);

            Assert.AreEqual(1, productos.Count);
            Assert.AreEqual("17404", productos[0].Producto);
            Assert.AreEqual(5, productos[0].Unidades);
            Assert.AreEqual(2, productos[0].PedidosVenta);
            Assert.AreEqual(HOY.AddDays(10), productos[0].FechaSalidaOrientativa);
        }

        // ---------- Ingredientes, YaTocabaPedir y la regla ----------

        [TestMethod]
        public void Ingredientes_ConPlazoPropio_DesglosanLaMismaFechaQueLaRegla()
        {
            PlazoProveedor propio = Propio(HOY.AddDays(-10));

            IngredientesFechaOrientativa i = ReglasFechasOrientativas.Ingredientes(HOY, propio, Generico);

            Assert.AreEqual(ReglaFechaOrientativaSinPedir.LlegadaEstimada(HOY, propio, Generico), i.FechaLlegadaOrientativa);
            Assert.AreEqual(new DateTime(2026, 11, 21), i.FechaLlegadaOrientativa);
            Assert.AreEqual(14, i.Cadencia);
            Assert.AreEqual(10, i.DiasDesdeUltimo);
            Assert.AreEqual(4, i.DiasHastaPedido);
            Assert.AreEqual(new DateTime(2026, 11, 17), i.FechaPedidoPrevista);
            Assert.AreEqual(4, i.PlazoDias);
            Assert.IsTrue(i.PlazoPropio);
            Assert.IsFalse(i.YaTocabaPedir);
        }

        [TestMethod]
        public void Ingredientes_YaTocabaPedirYPlazoGenerico()
        {
            PlazoProveedor propio = Propio(HOY.AddDays(-24), recepciones: 2);

            IngredientesFechaOrientativa i = ReglasFechasOrientativas.Ingredientes(HOY, propio, Generico);

            Assert.IsTrue(i.YaTocabaPedir);
            Assert.AreEqual(1, i.DiasHastaPedido, "ya tocaba: la regla dice «mañana»");
            Assert.IsFalse(i.PlazoPropio);
            Assert.AreEqual(11, i.PlazoDias);
            Assert.AreEqual(HOY.AddDays(12), i.FechaLlegadaOrientativa);
            Assert.AreEqual(ReglaFechaOrientativaSinPedir.LlegadaEstimada(HOY, propio, Generico), i.FechaLlegadaOrientativa);
        }

        [TestMethod]
        public void Ingredientes_SinCadenciaOSinPlazo_Null()
        {
            PlazoProveedor sinCadencia = Propio(HOY.AddDays(-3));
            sinCadencia.IntervaloPedidosMediano = null;
            Assert.IsNull(ReglasFechasOrientativas.Ingredientes(HOY, sinCadencia, Generico));
            Assert.IsNull(ReglasFechasOrientativas.Ingredientes(HOY, Propio(HOY.AddDays(-3), recepciones: 1), null));
            Assert.IsNull(ReglasFechasOrientativas.Ingredientes(HOY, null, Generico));
        }

        [TestMethod]
        public void YaTocabaPedir_DesdeElUltimoIgualOMayorQueLaCadencia()
        {
            Assert.IsTrue(ReglasFechasOrientativas.YaTocabaPedir(14, 14));
            Assert.IsTrue(ReglasFechasOrientativas.YaTocabaPedir(20, 14));
            Assert.IsFalse(ReglasFechasOrientativas.YaTocabaPedir(13, 14));
        }

        // ---------- Episodios ----------

        [TestMethod]
        public void PrimeraDelEpisodio_ElFinDeSemanaNoCortaElEpisodio()
        {
            Assert.IsTrue(ReglasFechasOrientativas.PrimeraDelEpisodio(LUNES, null, FinDeSemana));
            Assert.IsFalse(ReglasFechasOrientativas.PrimeraDelEpisodio(LUNES, HOY, FinDeSemana), "foto el viernes (laborable anterior): sigue el episodio");
            Assert.IsFalse(ReglasFechasOrientativas.PrimeraDelEpisodio(LUNES, HOY.AddDays(1), FinDeSemana), "foto el sábado");
            Assert.IsTrue(ReglasFechasOrientativas.PrimeraDelEpisodio(LUNES, HOY.AddDays(-1), FinDeSemana), "el viernes no estaba: empieza otro");
            Assert.IsFalse(ReglasFechasOrientativas.PrimeraDelEpisodio(HOY, HOY.AddDays(-1), FinDeSemana), "foto el día anterior");
        }

        [TestMethod]
        public void AnteriorLaborable_SaltaFinesDeSemanaYFestivos()
        {
            Assert.AreEqual(HOY, ReglasFechasOrientativas.AnteriorLaborable(LUNES, FinDeSemana));
            Assert.AreEqual(HOY.AddDays(-1), ReglasFechasOrientativas.AnteriorLaborable(LUNES, d => FinDeSemana(d) || d == HOY));
            Assert.AreEqual(HOY.AddDays(-1), ReglasFechasOrientativas.AnteriorLaborable(HOY, FinDeSemana));
        }

        [TestMethod]
        public void ConstruirFoto_ConTodosLosIngredientes()
        {
            var plazos = new PlazosProveedores(new[] { Propio(HOY.AddDays(-10)), Generico });
            var producto = new ProductoSinPedir { Producto = "17404", Unidades = 5, PedidosVenta = 2, FechaSalidaOrientativa = new DateTime(2026, 11, 23) };

            FotoFechaOrientativa foto = ReglasFechasOrientativas.ConstruirFoto(HOY, "1", producto, " 65 ", plazos, null, FinDeSemana);

            Assert.AreEqual(HOY, foto.DiaFoto);
            Assert.AreEqual("65", foto.Proveedor);
            Assert.AreEqual(5, foto.Unidades);
            Assert.AreEqual(2, foto.PedidosVenta);
            Assert.AreEqual(HOY.AddDays(-10), foto.UltimoPedido);
            Assert.AreEqual(new DateTime(2026, 11, 21), foto.FechaLlegadaOrientativa);
            Assert.AreEqual(new DateTime(2026, 11, 23), foto.FechaSalidaOrientativa);
            Assert.IsTrue(foto.PrimeraDelEpisodio);
            Assert.IsNull(ReglasFechasOrientativas.ConstruirFoto(HOY, "1", producto, null, plazos, null, FinDeSemana), "sin proveedor habitual, nada");
            Assert.IsNull(ReglasFechasOrientativas.ConstruirFoto(HOY, "1", producto, "OTRO", plazos, null, FinDeSemana), "proveedor sin fila: la regla no da fecha");
        }

        // ---------- Resolver y caducar ----------

        private static FotoOrientativaAbierta Abierta(DateTime? diaFoto = null) => new FotoOrientativaAbierta
        {
            Id = 7,
            DiaFoto = diaFoto ?? HOY,
            FechaPedidoPrevista = new DateTime(2026, 11, 17),
            FechaLlegadaOrientativa = new DateTime(2026, 11, 21)
        };

        [TestMethod]
        public void Resolver_ConPedidoYAlbaran_ResueltaConLosDosErrores()
        {
            DateTime ahora = new DateTime(2026, 11, 26, 3, 50, 0);
            var real = new PedidoRealOrientativa { Empresa = "1  ", Pedido = 5001, FechaPedido = new DateTime(2026, 11, 19), DeEnvios = true, FechaAlbaran = new DateTime(2026, 11, 25) };

            ResolucionFechaOrientativa r = ReglasFechasOrientativas.Resolver(Abierta(), real, ahora);

            Assert.IsTrue(r.Cambia);
            Assert.AreEqual("1", r.EmpresaPedidoReal);
            Assert.AreEqual(5001, r.PedidoReal);
            Assert.AreEqual(2, r.ErrorPedidoDias, "se pidió dos días después de lo previsto");
            Assert.AreEqual(4, r.ErrorLlegadaDias, "llegó cuatro días después de la orientativa: se quedó corta");
            Assert.AreEqual(ahora, r.ResueltaEl);
            Assert.IsFalse(r.Caducada);
            Assert.IsTrue(r.FechaPedidoRealDeEnvios.Value);
        }

        [TestMethod]
        public void Resolver_PedidoSinAlbaran_ApuntaElPedidoYSigueAbierta()
        {
            var real = new PedidoRealOrientativa { Empresa = "3", Pedido = 5001, FechaPedido = new DateTime(2026, 11, 15) };

            ResolucionFechaOrientativa r = ReglasFechasOrientativas.Resolver(Abierta(), real, HOY.AddDays(5));

            Assert.IsTrue(r.Cambia);
            Assert.AreEqual(-2, r.ErrorPedidoDias);
            Assert.IsNull(r.ErrorLlegadaDias);
            Assert.IsNull(r.ResueltaEl);
            Assert.IsFalse(r.Caducada);
        }

        [TestMethod]
        public void Resolver_SinCambiosRespectoALoGuardado_NoSeEscribe()
        {
            FotoOrientativaAbierta foto = Abierta();
            foto.EmpresaPedidoReal = "3";
            foto.PedidoReal = 5001;
            foto.FechaPedidoReal = new DateTime(2026, 11, 15);
            var real = new PedidoRealOrientativa { Empresa = "3", Pedido = 5001, FechaPedido = new DateTime(2026, 11, 15) };

            Assert.IsFalse(ReglasFechasOrientativas.Resolver(foto, real, HOY.AddDays(5)).Cambia);
            Assert.IsFalse(ReglasFechasOrientativas.Resolver(Abierta(), null, HOY.AddDays(5)).Cambia, "sin pedido y sin caducar: nada");
        }

        [TestMethod]
        public void Resolver_SinLlegarConMasDe120Dias_Caduca()
        {
            ResolucionFechaOrientativa sinPedir = ReglasFechasOrientativas.Resolver(Abierta(), null, HOY.AddDays(121));
            Assert.IsTrue(sinPedir.Caducada);
            Assert.IsTrue(sinPedir.Cambia);
            Assert.IsNull(sinPedir.ResueltaEl);

            Assert.IsFalse(ReglasFechasOrientativas.Resolver(Abierta(), null, HOY.AddDays(120)).Caducada, "120 justos aún no");

            var pedidoSinLlegar = new PedidoRealOrientativa { Empresa = "1", Pedido = 1, FechaPedido = HOY.AddDays(3) };
            ResolucionFechaOrientativa r = ReglasFechasOrientativas.Resolver(Abierta(), pedidoSinLlegar, HOY.AddDays(130));
            Assert.IsTrue(r.Caducada, "pedida pero sin llegar: también caduca, con el pedido apuntado");
            Assert.AreEqual(1, r.PedidoReal);

            var llegado = new PedidoRealOrientativa { Empresa = "1", Pedido = 1, FechaPedido = HOY.AddDays(3), FechaAlbaran = HOY.AddDays(125) };
            Assert.IsFalse(ReglasFechasOrientativas.Resolver(Abierta(), llegado, HOY.AddDays(130)).Caducada, "si llegó, se resuelve aunque sea tarde");
        }

        // ---------- Resumen ----------

        private static FilaErrorOrientativa Fila(string proveedor, bool yaTocaba, int pedido, int llegada) =>
            new FilaErrorOrientativa { Proveedor = proveedor, YaTocabaPedir = yaTocaba, ErrorPedidoDias = pedido, ErrorLlegadaDias = llegada };

        [TestMethod]
        public void Estadisticas_MediasMedianasYPorcentajeCorta()
        {
            EstadisticasFechasOrientativas e = ReglasFechasOrientativas.Estadisticas(new[]
            {
                Fila("65", false, 1, 4), Fila("65", false, 0, -2), Fila("65", true, 3, 0), Fila("65", true, 2, 6)
            });

            Assert.AreEqual(4, e.Casos);
            Assert.AreEqual(1.5m, e.ErrorPedidoMedio);
            Assert.AreEqual(1, e.ErrorPedidoMediano);
            Assert.AreEqual(2m, e.ErrorLlegadaMedio);
            Assert.AreEqual(0, e.ErrorLlegadaMediano, "mediana por rango más cercano, como los plazos");
            Assert.AreEqual(50m, e.PorcentajeCorta);
            Assert.AreEqual(0, ReglasFechasOrientativas.Estadisticas(null).Casos);
            Assert.IsNull(ReglasFechasOrientativas.Estadisticas(null).PorcentajeCorta);
        }

        [TestMethod]
        public void Resumir_PorProveedorDeMasAMenosCasosConTotalesYSeparandoYaTocaba()
        {
            ResumenFechasOrientativasDTO resumen = ReglasFechasOrientativas.Resumir(new[]
            {
                Fila("65", false, 1, 4), Fila("65", true, 3, 0), Fila("12", false, 0, -1), Fila("65 ", true, 2, 6)
            }, 6, HOY.AddMonths(-6), abiertas: 9, caducadas: 2);

            Assert.AreEqual(4, resumen.Totales.Todas.Casos);
            Assert.IsNull(resumen.Totales.Proveedor);
            Assert.AreEqual(2, resumen.Proveedores.Count);
            Assert.AreEqual("65", resumen.Proveedores[0].Proveedor);
            Assert.AreEqual(3, resumen.Proveedores[0].Todas.Casos);
            Assert.AreEqual(2, resumen.Proveedores[0].YaTocabaPedir.Casos);
            Assert.AreEqual(1, resumen.Proveedores[0].NoTocabaPedir.Casos);
            Assert.AreEqual(9, resumen.Abiertas);
            Assert.AreEqual(2, resumen.Caducadas);
            Assert.AreEqual(6, resumen.Meses);
        }

        // ---------- Servicio (FOTO + RESOLVER) ----------

        private IRepositorioFechasOrientativas repositorio;
        private List<FotoFechaOrientativa> insertadas;
        private List<ResolucionFechaOrientativa> escritas;
        private List<Exception> errores;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = A.Fake<IRepositorioFechasOrientativas>();
            insertadas = new List<FotoFechaOrientativa>();
            escritas = new List<ResolucionFechaOrientativa>();
            errores = new List<Exception>();
            A.CallTo(() => repositorio.TablaExiste()).Returns(true);
            A.CallTo(() => repositorio.LeerPedidosVentaPendientes("1")).Returns(new List<int> { 100, 101, 102 });
            A.CallTo(() => repositorio.UltimasFotosAnteriores("1", HOY, HOY.AddDays(-ReglasFechasOrientativas.DIAS_BUSCAR_EPISODIO)))
                .Returns(new Dictionary<string, DateTime> { ["20000"] = HOY.AddDays(-1) });
            A.CallTo(() => repositorio.Insertar(A<FotoFechaOrientativa>._)).Invokes((FotoFechaOrientativa f) => insertadas.Add(f)).Returns(true);
            A.CallTo(() => repositorio.IdsAbiertas("1")).Returns(new List<int>());
            A.CallTo(() => repositorio.Actualizar(A<IReadOnlyCollection<ResolucionFechaOrientativa>>._))
                .Invokes((IReadOnlyCollection<ResolucionFechaOrientativa> r) => escritas.AddRange(r)).Returns(0);
            FechasOrientativasJobsService.OlvidarAvisos();
        }

        private ServicioFechasOrientativas Servicio(Func<string, int, Task<FechaEntregaAgenciaDTO>> calcular = null) => new ServicioFechasOrientativas(
            repositorio,
            calcular ?? ((empresa, pedido) => Task.FromResult(new FechaEntregaAgenciaDTO
            {
                Desglose = new List<PartidaFechaEntregaAgenciaDTO> { SinPedir(pedido == 100 ? "17404" : "20000", 1, HOY.AddDays(10)) }
            })),
            empresa => new PlazosProveedores(new[] { Propio(HOY.AddDays(-10)), Generico }),
            (empresa, productos) => productos.ToDictionary(p => p, p => "65"),
            () => HOY.AddHours(3).AddMinutes(50),
            FinDeSemana,
            errores.Add);

        [TestMethod]
        public async Task Procesar_Foto_InsertaUnaFilaPorProductoConElEpisodio()
        {
            ResultadoFechasOrientativas resultado = await Servicio().Procesar("1");

            Assert.IsTrue(resultado.Hecho);
            Assert.AreEqual(3, resultado.PedidosVenta);
            Assert.AreEqual(2, insertadas.Count);
            FotoFechaOrientativa nuevo = insertadas.Single(f => f.Producto == "17404");
            FotoFechaOrientativa sigue = insertadas.Single(f => f.Producto == "20000");
            Assert.IsTrue(nuevo.PrimeraDelEpisodio);
            Assert.IsFalse(sigue.PrimeraDelEpisodio, "ayer ya tenía foto");
            Assert.AreEqual(2, sigue.PedidosVenta);
            Assert.AreEqual(1, resultado.Primeras);
            Assert.AreEqual(0, errores.Count);
        }

        [TestMethod]
        public async Task Procesar_UnPedidoQueFalla_NoImpideLosDemasYUnaSolaEntradaEnElmah()
        {
            ResultadoFechasOrientativas resultado = await Servicio((empresa, pedido) => pedido == 101
                ? throw new InvalidOperationException("se cayó")
                : Task.FromResult(new FechaEntregaAgenciaDTO { Desglose = new List<PartidaFechaEntregaAgenciaDTO> { SinPedir("17404", 1, HOY.AddDays(10)) } })).Procesar("1");

            Assert.AreEqual(1, resultado.PedidosConError);
            Assert.AreEqual(1, insertadas.Count);
            Assert.AreEqual(1, errores.Count);
        }

        [TestMethod]
        public async Task Procesar_SiLaFotoFalla_ElResolverSigue()
        {
            A.CallTo(() => repositorio.LeerPedidosVentaPendientes("1")).Throws(new InvalidOperationException("sin conexión"));
            A.CallTo(() => repositorio.IdsAbiertas("1")).Returns(new List<int> { 5 });
            A.CallTo(() => repositorio.LeerAbiertas("1", 5, 5)).Returns(new List<FotoAbiertaConPedido>
            {
                new FotoAbiertaConPedido { Foto = Abierta(HOY.AddDays(-200)) }
            });

            ResultadoFechasOrientativas resultado = await Servicio().Procesar("1");

            Assert.AreEqual(1, errores.Count);
            Assert.AreEqual(1, resultado.Caducadas);
            Assert.AreEqual(1, escritas.Count);
        }

        [TestMethod]
        public async Task Procesar_Resolver_PorLotesYSoloEscribeLoQueCambia()
        {
            List<int> ids = Enumerable.Range(1, ServicioFechasOrientativas.LOTE_RESOLVER + 3).ToList();
            A.CallTo(() => repositorio.IdsAbiertas("1")).Returns(ids);
            A.CallTo(() => repositorio.LeerAbiertas("1", 1, ServicioFechasOrientativas.LOTE_RESOLVER)).Returns(new List<FotoAbiertaConPedido>
            {
                new FotoAbiertaConPedido
                {
                    Foto = Abierta(HOY.AddDays(-15)),
                    Real = new PedidoRealOrientativa { Empresa = "1", Pedido = 9, FechaPedido = HOY.AddDays(-12), FechaAlbaran = HOY.AddDays(-5) }
                },
                new FotoAbiertaConPedido { Foto = Abierta(HOY.AddDays(-3)) }
            });
            A.CallTo(() => repositorio.LeerAbiertas("1", ServicioFechasOrientativas.LOTE_RESOLVER + 1, ServicioFechasOrientativas.LOTE_RESOLVER + 3))
                .Returns(new List<FotoAbiertaConPedido>());

            ResultadoFechasOrientativas resultado = await Servicio().Procesar("1");

            A.CallTo(() => repositorio.LeerAbiertas("1", A<int>._, A<int>._)).MustHaveHappenedTwiceExactly();
            Assert.AreEqual(2, resultado.Abiertas);
            Assert.AreEqual(1, resultado.Resueltas);
            Assert.AreEqual(1, escritas.Count, "la que no cambia no se escribe");
            Assert.AreEqual(9, escritas[0].PedidoReal);
        }

        [TestMethod]
        public async Task Procesar_SinPlazos_NoHaceFotoNiCalculaElMotor()
        {
            int calculados = 0;
            var servicio = new ServicioFechasOrientativas(repositorio, (e, p) => { calculados++; return Task.FromResult(new FechaEntregaAgenciaDTO()); },
                empresa => null, (e, p) => new Dictionary<string, string>(), () => HOY, FinDeSemana, errores.Add);

            ResultadoFechasOrientativas resultado = await servicio.Procesar("1");

            Assert.IsTrue(resultado.Hecho);
            Assert.AreEqual(0, calculados);
            A.CallTo(() => repositorio.Insertar(A<FotoFechaOrientativa>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Job_SinTabla_NoHaceNadaYAvisaUnaSolaVez()
        {
            A.CallTo(() => repositorio.TablaExiste()).Returns(false);

            ResultadoFechasOrientativas primera = await FechasOrientativasJobsService.Ejecutar(Servicio(), errores.Add);
            _ = await FechasOrientativasJobsService.Ejecutar(Servicio(), errores.Add);

            Assert.IsFalse(primera.Hecho);
            Assert.AreEqual(ServicioFechasOrientativas.MENSAJE_SIN_TABLA, primera.Mensaje);
            Assert.AreEqual(1, errores.Count);
            A.CallTo(() => repositorio.LeerPedidosVentaPendientes(A<string>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.IdsAbiertas(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Job_HoraYCola()
        {
            Assert.AreEqual("fechas-orientativas-sin-pedir", FechasOrientativasJobsService.JOB_ID);
            Assert.AreEqual("50 3 * * *", FechasOrientativasJobsService.CRON);
        }

        [TestMethod]
        public void Resumen_LimitaLosMesesYLeeDesdeLaFechaQueToca()
        {
            A.CallTo(() => repositorio.LeerResueltas("1", A<DateTime>._)).Returns(new List<FilaErrorOrientativa> { Fila("65", false, 1, 2) });
            A.CallTo(() => repositorio.ContarPendientes("1", A<DateTime>._)).Returns((4, 1));

            ResumenFechasOrientativasDTO resumen = Servicio().Resumen("3", 99);

            Assert.AreEqual(ServicioFechasOrientativas.MESES_MAXIMO, resumen.Meses);
            Assert.AreEqual(HOY.AddMonths(-24), resumen.Desde);
            Assert.AreEqual(1, resumen.Totales.Todas.Casos);
            Assert.AreEqual(4, resumen.Abiertas);
            A.CallTo(() => repositorio.LeerResueltas("1", HOY.AddMonths(-24))).MustHaveHappenedOnceExactly();
        }
    }
}
