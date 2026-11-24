using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Campanas;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Campanas
{
    [TestClass]
    public class ReglaCampanaBloqueTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 23);
        private static readonly DateTime DESDE = new DateTime(2026, 11, 25);
        private static readonly DateTime HASTA = new DateTime(2026, 11, 30);

        private static PeticionCampanaBloqueDTO Peticion(params ReglaCampanaBloqueDTO[] reglas) => new PeticionCampanaBloqueDTO
        {
            Nombre = "Black Friday 2026",
            FechaDesde = DESDE,
            FechaHasta = HASTA,
            Audiencia = 2,
            Reglas = reglas.ToList(),
            Excepciones = new List<ExcepcionCampanaBloqueDTO>(),
            DesbloquearAplicarDto = true
        };

        private static ReglaCampanaBloqueDTO Regla(string familia = "Lisap", decimal descuento = 0.20M) =>
            new ReglaCampanaBloqueDTO { Familia = familia, Descuento = descuento };

        private static ProductoCandidatoCampana Ficha(string numero, string familia = "Lisap", string nombre = "CHAMPU", string grupo = "PEL",
            string subgrupo = "CHA", decimal? pvp = 10M, bool aplicarDto = true, short? estado = 0) => new ProductoCandidatoCampana
            {
                Numero = numero, Nombre = nombre, Familia = familia, Grupo = grupo, SubGrupo = subgrupo, PVP = pvp, AplicarDto = aplicarDto, Estado = estado
            };

        private static LineaCampanaBloqueDTO Linea(ResultadoCalculoCampanaBloque r, string producto) => r.Lineas.Single(l => l.Producto == producto);

        private static ResultadoCalculoCampanaBloque Calcular(PeticionCampanaBloqueDTO p, IEnumerable<ProductoCandidatoCampana> fichas,
            IEnumerable<FilaExistenteCampana> existentes = null)
        {
            Assert.IsNull(ReglaCampanaBloque.Validar(p, HOY));
            return ReglaCampanaBloque.Calcular(p, fichas, existentes ?? new List<FilaExistenteCampana>());
        }

        // ---------------------------------------------------------------- solo en stock (NestoAPI#889)

        [TestMethod]
        public void Calcular_CondicionStock_LaPeticionManda_YLaReglaOLaExcepcionLaCambian()
        {
            PeticionCampanaBloqueDTO p = Peticion(Regla(), new ReglaCampanaBloqueDTO { Familia = "Otra", Descuento = 0.10M, CondicionStock = false });
            p.CondicionStock = true;
            p.Excepciones = new List<ExcepcionCampanaBloqueDTO> { new ExcepcionCampanaBloqueDTO { Producto = "2", Descuento = 0.30M, CondicionStock = false } };

            ResultadoCalculoCampanaBloque r = Calcular(p, new[] { Ficha("1"), Ficha("2"), Ficha("3", familia: "Otra"), Ficha("4", grupo: "MTP") });

            Assert.IsTrue(Linea(r, "1").CondicionStock, "La de la petición");
            Assert.IsFalse(Linea(r, "2").CondicionStock, "La excepción dice que no");
            Assert.IsFalse(Linea(r, "3").CondicionStock, "Su regla dice que no");
            Assert.IsFalse(Linea(r, "4").CondicionStock, "La que queda fuera no lleva marca");
            Assert.AreEqual(1, ReglaCampanaBloque.Totales(r.Lineas).Total.CondicionStock);
        }

        [TestMethod]
        public void Calcular_CondicionStock_YaEstaba_LlevaLaMarca()
        {
            PeticionCampanaBloqueDTO p = Peticion(Regla());
            p.CondicionStock = true;

            ResultadoCalculoCampanaBloque r = Calcular(p, new[] { Ficha("1") },
                new[] { new FilaExistenteCampana { Id = 7, Producto = "1", Descuento = 0.20M, Campana = "Black Friday 2026" } });

            Assert.AreEqual(EstadoLineaCampanaBloque.YaEstaba, Linea(r, "1").Estado);
            Assert.IsTrue(Linea(r, "1").CondicionStock);
        }

        // ---------------------------------------------------------------- regla por familia

        [TestMethod]
        public void Calcular_ReglaPorFamilia_EntranTodosLosDeLaFamiliaConSuDescuentoYPrecio()
        {
            ResultadoCalculoCampanaBloque r = Calcular(Peticion(Regla()), new[] { Ficha("1"), Ficha("2", pvp: 15M), Ficha("3", familia: "Otra") });

            Assert.AreEqual(2, r.Lineas.Count, "La de otra familia no es candidata");
            LineaCampanaBloqueDTO l = Linea(r, "2");
            Assert.AreEqual(EstadoLineaCampanaBloque.Entra, l.Estado);
            Assert.AreEqual(0.20M, l.Descuento);
            Assert.AreEqual(12M, l.PrecioResultante);
            Assert.AreEqual((byte)2, l.Audiencia);
            Assert.AreEqual(ReglaCampanaBloque.ORIGEN_REGLA, l.Origen);
        }

        [TestMethod]
        public void Calcular_AudienciaPorRegla_MandaSobreLaDeLaPeticion()
        {
            ReglaCampanaBloqueDTO soloProfesionales = Regla("Eva");
            soloProfesionales.Audiencia = 1;
            ResultadoCalculoCampanaBloque r = Calcular(Peticion(Regla(), soloProfesionales), new[] { Ficha("1"), Ficha("2", familia: "Eva") });

            Assert.AreEqual((byte)2, Linea(r, "1").Audiencia);
            Assert.AreEqual((byte)1, Linea(r, "2").Audiencia);
        }

        [TestMethod]
        public void Calcular_FamiliaSinProductos_Avisa()
        {
            ResultadoCalculoCampanaBloque r = Calcular(Peticion(Regla("Lisapp")), new[] { Ficha("1") });

            Assert.AreEqual(0, r.Lineas.Count);
            Assert.IsTrue(r.Avisos.Single().Contains("Lisapp"));
        }

        // ---------------------------------------------------------------- exclusiones

        [TestMethod]
        public void Calcular_ExcluidoPorReferenciaGlobalODeLaRegla()
        {
            ReglaCampanaBloqueDTO regla = Regla();
            regla.ProductosExcluidos = new List<string> { "2" };
            PeticionCampanaBloqueDTO p = Peticion(regla);
            p.ProductosExcluidos = new List<string> { " 1 " };

            ResultadoCalculoCampanaBloque r = Calcular(p, new[] { Ficha("1"), Ficha("2"), Ficha("3") });

            Assert.AreEqual(MotivosCampanaBloque.EXCLUIDO_REFERENCIA, Linea(r, "1").Motivo);
            Assert.AreEqual(MotivosCampanaBloque.EXCLUIDO_REFERENCIA, Linea(r, "2").Motivo);
            Assert.AreEqual(EstadoLineaCampanaBloque.Entra, Linea(r, "3").Estado);
        }

        [TestMethod]
        public void Calcular_ExcluidoPorTexto_SinMayusculasNiTildesYPalabraEntera()
        {
            ReglaCampanaBloqueDTO regla = Regla();
            regla.TextosExcluidos = new List<string> { "pack", "lóte" };
            ResultadoCalculoCampanaBloque r = Calcular(Peticion(regla), new[]
            {
                Ficha("1", nombre: "PACK CHAMPU+MASCARILLA"),
                Ficha("2", nombre: "LOTE NAVIDAD"),
                Ficha("3", nombre: "PACKAGING CHAMPU"),
                Ficha("4", nombre: "CHAMPU (PACK)")
            });

            Assert.AreEqual(MotivosCampanaBloque.EXCLUIDO_TEXTO, Linea(r, "1").Motivo);
            Assert.AreEqual(MotivosCampanaBloque.EXCLUIDO_TEXTO, Linea(r, "2").Motivo);
            Assert.AreEqual(EstadoLineaCampanaBloque.Entra, Linea(r, "3").Estado, "«packaging» no es la palabra «pack»");
            Assert.AreEqual(MotivosCampanaBloque.EXCLUIDO_TEXTO, Linea(r, "4").Motivo);
        }

        [TestMethod]
        public void Calcular_ExcluidoPorGrupoYSubgrupo()
        {
            ReglaCampanaBloqueDTO regla = Regla();
            regla.GruposExcluidos = new List<string> { "ACC" };
            regla.SubgruposExcluidos = new List<string> { "cos/out", "TIN" };
            ResultadoCalculoCampanaBloque r = Calcular(Peticion(regla), new[]
            {
                Ficha("1", grupo: "ACC"),
                Ficha("2", grupo: "COS", subgrupo: "OUT"),
                Ficha("3", grupo: "PEL", subgrupo: "OUT"),
                Ficha("4", grupo: "PEL", subgrupo: "TIN")
            });

            Assert.AreEqual(MotivosCampanaBloque.EXCLUIDO_GRUPO, Linea(r, "1").Motivo);
            Assert.AreEqual(MotivosCampanaBloque.EXCLUIDO_SUBGRUPO, Linea(r, "2").Motivo);
            Assert.AreEqual(EstadoLineaCampanaBloque.Entra, Linea(r, "3").Estado, "COS/OUT solo excluye el OUT de COS");
            Assert.AreEqual(MotivosCampanaBloque.EXCLUIDO_SUBGRUPO, Linea(r, "4").Motivo);
        }

        [TestMethod]
        public void Calcular_SiempreFueraEstadoNegativoMateriasPrimasMuestrasYSinPvp()
        {
            ResultadoCalculoCampanaBloque r = Calcular(Peticion(Regla()), new[]
            {
                Ficha("1", estado: -1), Ficha("2", estado: null), Ficha("3", grupo: "MTP"), Ficha("4", subgrupo: "MMP"),
                Ficha("5", pvp: null), Ficha("6", pvp: 0M)
            });

            Assert.AreEqual(MotivosCampanaBloque.ESTADO, Linea(r, "1").Motivo);
            Assert.AreEqual(MotivosCampanaBloque.ESTADO, Linea(r, "2").Motivo);
            Assert.AreEqual(MotivosCampanaBloque.MATERIA_PRIMA, Linea(r, "3").Motivo);
            Assert.AreEqual(MotivosCampanaBloque.MUESTRA, Linea(r, "4").Motivo);
            Assert.AreEqual(MotivosCampanaBloque.SIN_PVP, Linea(r, "5").Motivo);
            Assert.AreEqual(MotivosCampanaBloque.SIN_PVP, Linea(r, "6").Motivo);
        }

        // ---------------------------------------------------------------- excepciones

        [TestMethod]
        public void Calcular_ExcepcionManda_SobreLaReglaYSusExclusionesDeTexto()
        {
            ReglaCampanaBloqueDTO regla = Regla();
            regla.TextosExcluidos = new List<string> { "pack" };
            PeticionCampanaBloqueDTO p = Peticion(regla);
            p.Excepciones.Add(new ExcepcionCampanaBloqueDTO { Producto = "1", Descuento = 0.35M, Audiencia = 1 });

            ResultadoCalculoCampanaBloque r = Calcular(p, new[] { Ficha("1", nombre: "PACK ESPECIAL"), Ficha("2") });

            Assert.AreEqual(2, r.Lineas.Count, "La excepción no sale dos veces");
            LineaCampanaBloqueDTO l = Linea(r, "1");
            Assert.AreEqual(EstadoLineaCampanaBloque.Entra, l.Estado);
            Assert.AreEqual(0.35M, l.Descuento);
            Assert.AreEqual((byte)1, l.Audiencia);
            Assert.AreEqual(ReglaCampanaBloque.ORIGEN_EXCEPCION, l.Origen);
            Assert.AreEqual(0.20M, Linea(r, "2").Descuento);
        }

        [TestMethod]
        public void Calcular_ExcepcionDeProductoQueNoExiste_SaleComoNoExiste()
        {
            PeticionCampanaBloqueDTO p = Peticion();
            p.Excepciones.Add(new ExcepcionCampanaBloqueDTO { Producto = "99999", Descuento = 0.10M });

            ResultadoCalculoCampanaBloque r = Calcular(p, new ProductoCandidatoCampana[0]);

            Assert.AreEqual(MotivosCampanaBloque.NO_EXISTE, Linea(r, "99999").Motivo);
        }

        [TestMethod]
        public void Calcular_ExcepcionDeMateriaPrima_SigueFuera()
        {
            PeticionCampanaBloqueDTO p = Peticion();
            p.Excepciones.Add(new ExcepcionCampanaBloqueDTO { Producto = "1", Descuento = 0.10M });

            ResultadoCalculoCampanaBloque r = Calcular(p, new[] { Ficha("1", grupo: "MTP") });

            Assert.AreEqual(MotivosCampanaBloque.MATERIA_PRIMA, Linea(r, "1").Motivo);
        }

        // ---------------------------------------------------------------- fichas bloqueadas

        [TestMethod]
        public void Calcular_FichaBloqueadaConDesbloqueo_EntraYSeDesbloquea()
        {
            ResultadoCalculoCampanaBloque r = Calcular(Peticion(Regla()), new[] { Ficha("1", aplicarDto: false), Ficha("2") });

            Assert.AreEqual(EstadoLineaCampanaBloque.Entra, Linea(r, "1").Estado);
            Assert.IsTrue(Linea(r, "1").DesbloquearFicha);
            Assert.IsFalse(Linea(r, "2").DesbloquearFicha);
        }

        [TestMethod]
        public void Calcular_FichaBloqueadaSinDesbloqueo_QuedaFuera()
        {
            PeticionCampanaBloqueDTO p = Peticion(Regla());
            p.DesbloquearAplicarDto = false;

            ResultadoCalculoCampanaBloque r = Calcular(p, new[] { Ficha("1", aplicarDto: false) });

            Assert.AreEqual(MotivosCampanaBloque.FICHA_BLOQUEADA, Linea(r, "1").Motivo);
            Assert.IsFalse(Linea(r, "1").DesbloquearFicha);
        }

        [TestMethod]
        public void Calcular_FichaExcluida_NoSeDesbloquea()
        {
            ReglaCampanaBloqueDTO regla = Regla();
            regla.ProductosExcluidos = new List<string> { "1" };
            ResultadoCalculoCampanaBloque r = Calcular(Peticion(regla), new[] { Ficha("1", aplicarDto: false) });

            Assert.IsFalse(Linea(r, "1").DesbloquearFicha);
            Assert.AreEqual(0, ReglaCampanaBloque.Totales(r.Lineas).Total.FichasDesbloqueadas);
        }

        // ---------------------------------------------------------------- solapes e idempotencia

        private static FilaExistenteCampana Existente(int id, string producto, decimal descuento, decimal? precio = null,
            DateTime? desde = null, DateTime? hasta = null, string campana = null) => new FilaExistenteCampana
            {
                Id = id, Producto = producto, Descuento = descuento, Precio = precio, FechaDesde = desde, FechaHasta = hasta, Campana = campana
            };

        [TestMethod]
        public void Calcular_PermanenteMejorOIgual_NoSeCreaYSeCuenta()
        {
            ResultadoCalculoCampanaBloque r = Calcular(Peticion(Regla()), new[] { Ficha("1"), Ficha("2") },
                new[] { Existente(10, "1", 0.25M), Existente(11, "2", 0.20M) });

            Assert.AreEqual(MotivosCampanaBloque.YA_TIENE_MEJOR, Linea(r, "1").Motivo);
            Assert.AreEqual(10, Linea(r, "1").FilaExistente);
            Assert.AreEqual(MotivosCampanaBloque.YA_TIENE_MEJOR, Linea(r, "2").Motivo, "Igual también: no se duplica");
            Assert.AreEqual(2, ReglaCampanaBloque.Totales(r.Lineas).Total.Solapes);
        }

        [TestMethod]
        public void Calcular_PermanentePeor_SeCreaYQuedaMarcadoComoSolape()
        {
            ResultadoCalculoCampanaBloque r = Calcular(Peticion(Regla()), new[] { Ficha("1") }, new[] { Existente(10, "1", 0.10M) });

            LineaCampanaBloqueDTO l = Linea(r, "1");
            Assert.AreEqual(EstadoLineaCampanaBloque.Entra, l.Estado);
            Assert.IsTrue(l.Solape);
            Assert.AreEqual(10, l.FilaExistente);
            TotalesCampanaBloqueDTO total = ReglaCampanaBloque.Totales(r.Lineas).Total;
            Assert.AreEqual(1, total.Entran);
            Assert.AreEqual(1, total.Solapes);
        }

        [TestMethod]
        public void Calcular_PrecioEspecialMejor_NoSeCrea()
        {
            // PVP 10: la campaña lo deja en 8; el precio especial en 7.
            ResultadoCalculoCampanaBloque r = Calcular(Peticion(Regla()), new[] { Ficha("1") }, new[] { Existente(10, "1", 0M, precio: 7M) });

            Assert.AreEqual(MotivosCampanaBloque.YA_TIENE_MEJOR, Linea(r, "1").Motivo);
        }

        [TestMethod]
        public void Calcular_FilaQueNoSeSolapaConLasFechas_NoCuenta()
        {
            ResultadoCalculoCampanaBloque r = Calcular(Peticion(Regla()), new[] { Ficha("1") },
                new[] { Existente(10, "1", 0.50M, desde: new DateTime(2026, 12, 1), hasta: new DateTime(2026, 12, 31)) });

            Assert.AreEqual(EstadoLineaCampanaBloque.Entra, Linea(r, "1").Estado);
            Assert.IsFalse(Linea(r, "1").Solape);
        }

        [TestMethod]
        public void Calcular_YaTieneSuFilaDeEstaCampana_YaEstaba()
        {
            ResultadoCalculoCampanaBloque r = Calcular(Peticion(Regla()), new[] { Ficha("1") },
                new[] { Existente(10, "1", 0.20M, desde: DESDE, hasta: HASTA, campana: "BLACK FRIDAY 2026") });

            Assert.AreEqual(EstadoLineaCampanaBloque.YaEstaba, Linea(r, "1").Estado);
            Assert.AreEqual(1, ReglaCampanaBloque.Totales(r.Lineas).Total.YaEstaban);
        }

        // ---------------------------------------------------------------- totales y validación

        [TestMethod]
        public void Totales_PorFamiliaYGeneral()
        {
            ReglaCampanaBloqueDTO regla = Regla();
            regla.ProductosExcluidos = new List<string> { "2" };
            ResultadoCalculoCampanaBloque r = Calcular(Peticion(regla, Regla("Eva", 0.3M)),
                new[] { Ficha("1"), Ficha("2"), Ficha("3", grupo: "MTP"), Ficha("4", familia: "Eva", aplicarDto: false) });

            (List<TotalesCampanaBloqueDTO> porFamilia, TotalesCampanaBloqueDTO total) = ReglaCampanaBloque.Totales(r.Lineas);

            TotalesCampanaBloqueDTO lisap = porFamilia.Single(t => t.Familia == "Lisap");
            Assert.AreEqual(3, lisap.Productos);
            Assert.AreEqual(1, lisap.Entran);
            Assert.AreEqual(1, lisap.FueraPorMotivo[MotivosCampanaBloque.EXCLUIDO_REFERENCIA]);
            Assert.AreEqual(1, lisap.FueraPorMotivo[MotivosCampanaBloque.MATERIA_PRIMA]);
            Assert.AreEqual(4, total.Productos);
            Assert.AreEqual(2, total.Entran);
            Assert.AreEqual(1, total.FichasDesbloqueadas);
        }

        [TestMethod]
        public void Muestra_ComoMuchoNPorFamiliaPrimeroLasQueEntran()
        {
            List<ProductoCandidatoCampana> fichas = Enumerable.Range(1, 30).Select(i => Ficha(i.ToString("00"), grupo: i == 1 ? "MTP" : "PEL")).ToList();
            ResultadoCalculoCampanaBloque r = Calcular(Peticion(Regla()), fichas);

            List<LineaCampanaBloqueDTO> muestra = ReglaCampanaBloque.Muestra(r.Lineas, 20);

            Assert.AreEqual(20, muestra.Count);
            Assert.IsTrue(muestra.All(l => l.Estado == EstadoLineaCampanaBloque.Entra));
        }

        [TestMethod]
        public void Validar_CasosQueSeRechazan()
        {
            Assert.IsNotNull(ReglaCampanaBloque.Validar(null, HOY));
            PeticionCampanaBloqueDTO sinNombre = Peticion(Regla()); sinNombre.Nombre = " ";
            Assert.IsNotNull(ReglaCampanaBloque.Validar(sinNombre, HOY));
            PeticionCampanaBloqueDTO sinFechas = Peticion(Regla()); sinFechas.FechaHasta = null;
            Assert.IsNotNull(ReglaCampanaBloque.Validar(sinFechas, HOY));
            PeticionCampanaBloqueDTO alReves = Peticion(Regla()); alReves.FechaDesde = HASTA.AddDays(1);
            Assert.IsNotNull(ReglaCampanaBloque.Validar(alReves, HOY));
            PeticionCampanaBloqueDTO pasada = Peticion(Regla());
            Assert.IsNotNull(ReglaCampanaBloque.Validar(pasada, HASTA.AddDays(1)), "Ya ha terminado");
            Assert.IsNotNull(ReglaCampanaBloque.Validar(Peticion(Regla("Lisap", 20M)), HOY), "20 en vez de 0,20");
            Assert.IsNotNull(ReglaCampanaBloque.Validar(Peticion(Regla(), Regla(" lisap ")), HOY), "Familia repetida");
            PeticionCampanaBloqueDTO sinAudiencia = Peticion(Regla()); sinAudiencia.Audiencia = null;
            Assert.IsNotNull(ReglaCampanaBloque.Validar(sinAudiencia, HOY));
            PeticionCampanaBloqueDTO audiencia3 = Peticion(Regla()); audiencia3.Audiencia = 3;
            Assert.IsNotNull(ReglaCampanaBloque.Validar(audiencia3, HOY));
            PeticionCampanaBloqueDTO otraEmpresa = Peticion(Regla()); otraEmpresa.Empresa = "3";
            Assert.IsNotNull(ReglaCampanaBloque.Validar(otraEmpresa, HOY));
            PeticionCampanaBloqueDTO vacia = Peticion();
            Assert.IsNotNull(ReglaCampanaBloque.Validar(vacia, HOY));
            PeticionCampanaBloqueDTO contradictoria = Peticion(Regla());
            contradictoria.ProductosExcluidos = new List<string> { "1" };
            contradictoria.Excepciones.Add(new ExcepcionCampanaBloqueDTO { Producto = "1", Descuento = 0.1M });
            Assert.IsNotNull(ReglaCampanaBloque.Validar(contradictoria, HOY));
        }

        [TestMethod]
        public void Validar_PeticionCorrecta_Vale()
        {
            Assert.IsNull(ReglaCampanaBloque.Validar(Peticion(Regla()), HOY));
        }

        [TestMethod]
        public void PuedeEstarEnLaTienda_FuturaNo_VigenteSi_RecienAcabadaSi_AntiguaNo()
        {
            Assert.IsFalse(ReglaCampanaBloque.PuedeEstarEnLaTienda(DESDE, HASTA, HOY, 2));
            Assert.IsTrue(ReglaCampanaBloque.PuedeEstarEnLaTienda(DESDE, HASTA, DESDE, 2));
            Assert.IsTrue(ReglaCampanaBloque.PuedeEstarEnLaTienda(DESDE, HASTA, HASTA.AddDays(2), 2));
            Assert.IsFalse(ReglaCampanaBloque.PuedeEstarEnLaTienda(DESDE, HASTA, HASTA.AddDays(3), 2));
            Assert.IsTrue(ReglaCampanaBloque.PuedeEstarEnLaTienda(null, null, HOY, 2));
        }
    }
}
