using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Aparatos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Aparatos
{
    /// <summary>NestoAPI#913, corte 2: enlazar cada aparato con su ficha de inmovilizado (propuesta, enlace con ensayo y descuadres).</summary>
    [TestClass]
    public class InmovilizadosAparatosTests
    {
        private static readonly DateTime D1 = new DateTime(2025, 3, 1);
        private static readonly DateTime D2 = new DateTime(2026, 11, 26);

        private static RegistroAparatoDTO R(int numero, string producto = "36929", string serie = "A32046", string inmovilizado = null, string empresa = "1", string cliente = null)
            => new RegistroAparatoDTO { Empresa = empresa, Numero = numero, Producto = producto, NumeroSerie = serie, Inmovilizado = inmovilizado, Cliente = cliente };

        private static InmovilizadoAparatoFila I(string numero, string descripcion, short estado = 0, string empresa = "1", string producto = null, string serie = null)
            => new InmovilizadoAparatoFila { Empresa = empresa, Numero = numero, Descripcion = descripcion, Estado = estado, FechaCreacion = D2, Producto = producto, NumeroSerie = serie };

        private static DatosAparatos Datos()
        {
            var datos = new DatosAparatos();
            datos.Productos.Add(new ProductoAparatoFila { Producto = "36929", Nombre = "DEPIBLEND", NecesitaNumSerie = true, EnListaAlquileres = true });
            datos.Productos.Add(new ProductoAparatoFila { Producto = "44276", Nombre = "RADIOFRECUENCIA", NecesitaNumSerie = true });
            return datos;
        }

        private static PropuestaInmovilizadoDTO Propuesta(DatosAparatos datos, string numero)
            => ReglaPropuestasInmovilizados.Calcular(datos).Propuestas.Single(p => p.Inmovilizado == numero);

        // ---------------------------------------------------------------- la serie escrita

        [DataTestMethod]
        [DataRow("DEPIBLEND ref.44276 n/s SHE-RFS009-2511111", "SHE-RFS009-2511111", "44276")]
        [DataRow("Depiblend N/S: AB-123", "AB-123", null)]
        [DataRow("Depiblend nº serie 12345", "12345", null)]
        [DataRow("Depiblend Nº de serie: 12345", "12345", null)]
        [DataRow("Depiblend S/N 12345.", "12345", null)]
        [DataRow("Depiblend n / s 12345", "12345", null)]
        [DataRow("Depiblend n/s12345", "12345", null)]
        [DataRow("Depiblend S/N:AB-1", "AB-1", null)]
        [DataRow("Depiblend número de serie X-9", "X-9", null)]
        [DataRow("Depiblend Núm. serie: 77", "77", null)]
        [DataRow("Depiblend num serie 77", "77", null)]
        public void Serie_SeLeeDelTexto(string descripcion, string serie, string referencia)
        {
            SerieLeidaInmovilizado leida = ReglaSerieInmovilizado.Leer(descripcion);
            Assert.AreEqual(serie, leida.Serie);
            Assert.AreEqual(referencia, leida.Referencia);
        }

        [DataTestMethod]
        [DataRow("Mesa de oficina")]
        [DataRow("Depiblend serie 5000")]
        [DataRow("Depiblend n/s")]
        [DataRow("Depiblend n/s :")]
        [DataRow("Ordenador Insignia")]
        [DataRow(null)]
        public void Serie_SinEtiquetaDeSerie_NoHay(string descripcion)
        {
            Assert.IsNull(ReglaSerieInmovilizado.Leer(descripcion));
        }

        [TestMethod]
        public void Serie_ConTextoDetras_LaEnteraYElPrimerTramo()
        {
            SerieLeidaInmovilizado leida = ReglaSerieInmovilizado.Leer("Depiblend n/s 12345 (demo Algete)");
            Assert.AreEqual("12345 (demo Algete)", leida.Serie);
            Assert.AreEqual("12345", leida.PrimerTramo);
            CollectionAssert.AreEqual(new[] { "12345 (demo Algete)", "12345" }, leida.Candidatas());
        }

        [TestMethod]
        public void Serie_LaColumnaMandaSobreElTexto()
        {
            SerieLeidaInmovilizado leida = ReglaSerieInmovilizado.DeFicha(I("1824", "Depiblend n/s OTRA", producto: "36929", serie: " A32046 "));
            Assert.AreEqual("A32046", leida.Serie);
            Assert.AreEqual("36929", leida.Referencia);
            Assert.AreEqual("Columna", ReglaSerieInmovilizado.Origen(I("1", "x", serie: "A")));
            Assert.AreEqual("Texto", ReglaSerieInmovilizado.Origen(I("1", "x")));
        }

        // ---------------------------------------------------------------- las propuestas

        [TestMethod]
        public void Propuesta_UnaCoincidenciaExacta_CertezaAltaConSuRegistro()
        {
            DatosAparatos datos = Datos();
            datos.Registros.Add(R(1242));
            datos.Inmovilizados.Add(I("1824", "DEPIBLEND ref.36929 n/s a32046"));

            PropuestaInmovilizadoDTO p = Propuesta(datos, "1824");

            Assert.AreEqual(CertezaEnlaceInmovilizado.UnaCoincidenciaExacta, p.Certeza);
            Assert.IsTrue(p.CertezaAlta);
            Assert.AreEqual(1242, p.RegistroPropuesto);
            Assert.AreEqual("I/1824", p.Codigo);
            Assert.AreEqual("Texto", p.OrigenSerie);
            Assert.IsNull(p.MotivoNoAplicable);
            Assert.AreEqual("DEPIBLEND", p.Candidatos.Single().Nombre);
        }

        [TestMethod]
        public void Propuesta_EnlazadoYa_NoSeProponeYSeCuenta()
        {
            DatosAparatos datos = Datos();
            datos.Registros.Add(R(1242, inmovilizado: "1824"));
            datos.Inmovilizados.Add(I("1824", "DEPIBLEND n/s A32046"));
            datos.Inmovilizados.Add(I("1825", "Mesa", estado: -1));
            datos.Inmovilizados.Add(I("1826", "Mesa de reuniones"));

            PropuestasInmovilizadosDTO r = ReglaPropuestasInmovilizados.Calcular(datos);

            Assert.AreEqual(3, r.TotalInmovilizados);
            Assert.AreEqual(1, r.Enlazados);
            Assert.AreEqual(1, r.Anulados);
            Assert.AreEqual(1, r.SinEnlazarSinSerie);
            Assert.AreEqual(0, r.Propuestas.Count);
        }

        [TestMethod]
        public void Propuesta_ElNumeroDelRegistroSeComparaSinRellenoNiCeros()
        {
            DatosAparatos datos = Datos();
            datos.Registros.Add(R(1242, inmovilizado: "0001824 "));
            datos.Inmovilizados.Add(I("1824", "DEPIBLEND n/s A32046"));
            Assert.AreEqual(1, ReglaPropuestasInmovilizados.Calcular(datos).Enlazados);
        }

        [TestMethod]
        public void Propuesta_ConReferencia_SoloEseProducto()
        {
            DatosAparatos datos = Datos();
            datos.Registros.Add(R(1242, producto: "36929", serie: "12345"));
            datos.Registros.Add(R(1300, producto: "44276", serie: "12345"));
            datos.Inmovilizados.Add(I("1824", "RF ref.44276 n/s 12345"));
            datos.Inmovilizados.Add(I("1825", "Aparato n/s 12345"));

            PropuestaInmovilizadoDTO conReferencia = Propuesta(datos, "1824");
            PropuestaInmovilizadoDTO sinReferencia = Propuesta(datos, "1825");

            Assert.IsTrue(conReferencia.CertezaAlta);
            Assert.AreEqual(1300, conReferencia.RegistroPropuesto);
            Assert.AreEqual(CertezaEnlaceInmovilizado.VariasCoincidencias, sinReferencia.Certeza);
            Assert.IsFalse(sinReferencia.CertezaAlta);
            Assert.AreEqual(2, sinReferencia.Candidatos.Count);
        }

        [TestMethod]
        public void Propuesta_ReferenciaEscritaDeOtroProducto_SoloPorParecido()
        {
            DatosAparatos datos = Datos();
            datos.Registros.Add(R(1242, producto: "36929", serie: "12345"));
            datos.Inmovilizados.Add(I("1824", "RF ref.44276 n/s 12345"));

            PropuestaInmovilizadoDTO p = Propuesta(datos, "1824");

            Assert.AreEqual(CertezaEnlaceInmovilizado.SoloPorParecido, p.Certeza);
            StringAssert.Contains(p.TextoCerteza, "referencia escrita (44276)");
            Assert.IsFalse(p.Candidatos.Single().Exacta);
        }

        [TestMethod]
        public void Propuesta_SerieEscritaDeOtraForma_SoloPorParecido()
        {
            DatosAparatos datos = Datos();
            datos.Registros.Add(R(1242, serie: "A32046"));
            datos.Inmovilizados.Add(I("1824", "DEPIBLEND n/s A32O46"));

            PropuestaInmovilizadoDTO p = Propuesta(datos, "1824");

            Assert.AreEqual(CertezaEnlaceInmovilizado.SoloPorParecido, p.Certeza);
            Assert.IsFalse(p.CertezaAlta);
            Assert.AreEqual("A32046", p.Candidatos.Single().NumeroSerie);
            Assert.IsTrue(p.Candidatos.Single().Aplicable, "a mano sí se puede");
        }

        [TestMethod]
        public void Propuesta_ConTextoDetras_CasaPorElPrimerTramo()
        {
            DatosAparatos datos = Datos();
            datos.Registros.Add(R(1242, serie: "A32046"));
            datos.Inmovilizados.Add(I("1824", "DEPIBLEND n/s A32046 demo"));

            PropuestaInmovilizadoDTO p = Propuesta(datos, "1824");

            Assert.IsTrue(p.CertezaAlta);
            Assert.AreEqual("A32046", p.Candidatos.Single().NumeroSerie);
        }

        [TestMethod]
        public void Propuesta_DescripcionCortadaA50_SoloPorParecido()
        {
            DatosAparatos datos = Datos();
            datos.Registros.Add(R(1242, producto: "44276", serie: "SHE-RFS009-2511111"));
            string descripcion = "RADIOFRECUENCIA PROFESIONAL ref.44276 n/s SHE-RFS0"; // 50 caracteres
            Assert.AreEqual(50, descripcion.Length);
            datos.Inmovilizados.Add(I("1824", descripcion));

            PropuestaInmovilizadoDTO p = Propuesta(datos, "1824");

            Assert.AreEqual(CertezaEnlaceInmovilizado.SoloPorParecido, p.Certeza);
            StringAssert.Contains(p.TextoCerteza, "cortada");
        }

        [TestMethod]
        public void Propuesta_SinAparato_Ninguna()
        {
            DatosAparatos datos = Datos();
            datos.Inmovilizados.Add(I("1824", "DEPIBLEND n/s ZZZ999"));
            Assert.AreEqual(CertezaEnlaceInmovilizado.Ninguna, Propuesta(datos, "1824").Certeza);
        }

        [TestMethod]
        public void Propuesta_AparatoSoloEnCompras_ExactaPeroSinRegistro_NoEsCertezaAlta()
        {
            DatosAparatos datos = Datos();
            datos.Compras.Add(new CompraAparatoFila { Pedido = 220931, Producto = "36929", NumeroSerie = "A32046", Cantidad = 1, Fecha = D1, Proveedor = "P1" });
            datos.Inmovilizados.Add(I("1824", "DEPIBLEND n/s A32046"));

            PropuestaInmovilizadoDTO p = Propuesta(datos, "1824");

            Assert.AreEqual(CertezaEnlaceInmovilizado.UnaCoincidenciaExacta, p.Certeza);
            Assert.IsFalse(p.CertezaAlta);
            StringAssert.Contains(p.MotivoNoAplicable, "no tiene registro");
            Assert.AreEqual(SituacionAparato.EnStock, p.Candidatos.Single().SituacionCalculada);
        }

        [TestMethod]
        public void Propuesta_SerieRepetidaEnElRegistro_NoEsCertezaAlta()
        {
            DatosAparatos datos = Datos();
            datos.Registros.Add(R(1242));
            datos.Registros.Add(R(1300));
            datos.Inmovilizados.Add(I("1824", "DEPIBLEND n/s A32046"));

            PropuestaInmovilizadoDTO p = Propuesta(datos, "1824");

            Assert.IsFalse(p.CertezaAlta);
            StringAssert.Contains(p.MotivoNoAplicable, "2 registros (1242, 1300)");
        }

        [TestMethod]
        public void Propuesta_RegistroConOtroInmovilizado_NoEsCertezaAlta()
        {
            DatosAparatos datos = Datos();
            datos.Registros.Add(R(1242, inmovilizado: "1700"));
            datos.Inmovilizados.Add(I("1700", "DEPIBLEND n/s A32046"));
            datos.Inmovilizados.Add(I("1824", "DEPIBLEND n/s A32046"));

            PropuestaInmovilizadoDTO p = Propuesta(datos, "1824");

            Assert.IsFalse(p.CertezaAlta);
            StringAssert.Contains(p.MotivoNoAplicable, "ya tiene el inmovilizado I/1700");
        }

        [TestMethod]
        public void Propuesta_RegistroDeOtraEmpresa_NoEsCertezaAlta()
        {
            DatosAparatos datos = Datos();
            datos.Registros.Add(R(1242, empresa: "3"));
            datos.Inmovilizados.Add(I("1824", "DEPIBLEND n/s A32046"));
            StringAssert.Contains(Propuesta(datos, "1824").MotivoNoAplicable, "empresa 3");
        }

        [TestMethod]
        public void Propuesta_DosInmovilizadosParaElMismoRegistro_NingunoEsCertezaAlta()
        {
            DatosAparatos datos = Datos();
            datos.Registros.Add(R(1242));
            datos.Inmovilizados.Add(I("1824", "DEPIBLEND n/s A32046"));
            datos.Inmovilizados.Add(I("1825", "DEPIBLEND demo n/s A32046"));

            PropuestasInmovilizadosDTO r = ReglaPropuestasInmovilizados.Calcular(datos);

            Assert.AreEqual(0, r.ConCertezaAlta);
            Assert.IsTrue(r.Propuestas.All(p => p.MotivoNoAplicable.Contains("I/1824") && p.MotivoNoAplicable.Contains("I/1825")));
        }

        [TestMethod]
        public void Propuesta_ConLaColumna_CasaPorLaColumna()
        {
            DatosAparatos datos = Datos();
            datos.ColumnasAparatoEnInmovilizados = true;
            datos.Registros.Add(R(1242));
            datos.Inmovilizados.Add(I("1824", "DEPIBLEND sin nada escrito", producto: "36929", serie: "A32046"));

            PropuestasInmovilizadosDTO r = ReglaPropuestasInmovilizados.Calcular(datos);

            Assert.IsTrue(r.ColumnasEnInmovilizados);
            Assert.AreEqual("Columna", r.Propuestas.Single().OrigenSerie);
            Assert.IsTrue(r.Propuestas.Single().CertezaAlta);
        }

        [TestMethod]
        public void AlReves_RegistroConInmovilizadoQueNoExisteAnuladoODeOtraSerie()
        {
            DatosAparatos datos = Datos();
            datos.Registros.Add(R(1, serie: "S1", inmovilizado: "9999"));
            datos.Registros.Add(R(2, serie: "S2", inmovilizado: "1700"));
            datos.Registros.Add(R(3, serie: "S3", inmovilizado: "1701"));
            datos.Registros.Add(R(4, serie: "S4", inmovilizado: "1702"));
            datos.Inmovilizados.Add(I("1700", "DEPIBLEND n/s S2", estado: -1));
            datos.Inmovilizados.Add(I("1701", "DEPIBLEND n/s OTRA"));
            datos.Inmovilizados.Add(I("1702", "DEPIBLEND n/s S-4"));      // la misma, escrita de otra forma: no es descuadre
            datos.Inmovilizados.Add(I("1824", "DEPIBLEND n/s S1"));

            List<AparatoSinInmovilizadoDTO> r = ReglaPropuestasInmovilizados.Calcular(datos).AparatosSinInmovilizado;

            Assert.AreEqual(3, r.Count);
            Assert.AreEqual(MotivoAparatoSinInmovilizado.FichaNoExiste, r[0].Motivo);
            Assert.AreEqual("I/1824", r[0].InmovilizadoPropuesto);
            Assert.AreEqual(MotivoAparatoSinInmovilizado.FichaAnulada, r[1].Motivo);
            Assert.AreEqual(MotivoAparatoSinInmovilizado.FichaConOtraSerie, r[2].Motivo);
            StringAssert.Contains(r[2].Texto, "su serie es OTRA");
        }

        // ---------------------------------------------------------------- qué se enlaza

        private static (DatosAparatos, PropuestasInmovilizadosDTO) ParaEnlazar()
        {
            DatosAparatos datos = Datos();
            datos.Registros.Add(R(1242, serie: "A32046"));
            datos.Registros.Add(R(1168, serie: "B1"));
            datos.Registros.Add(R(1500, serie: "C1", inmovilizado: "1600"));
            datos.Inmovilizados.Add(I("1824", "DEPIBLEND n/s A32046"));   // certeza alta
            datos.Inmovilizados.Add(I("1825", "DEPIBLEND n/s B-0001"));   // parecido
            datos.Inmovilizados.Add(I("1826", "DEPIBLEND", estado: -1));
            datos.Inmovilizados.Add(I("1600", "DEPIBLEND n/s C1"));
            return (datos, ReglaPropuestasInmovilizados.Calcular(datos));
        }

        [TestMethod]
        public void Enlace_EnBloque_SoloLasDeCertezaAlta()
        {
            (DatosAparatos datos, PropuestasInmovilizadosDTO propuestas) = ParaEnlazar();

            List<FilaEnlaceInmovilizadoDTO> filas = ReglaEnlaceInmovilizados.Planificar(new PeticionEnlaceInmovilizadosDTO { TodasLasDeCertezaAlta = true }, datos, propuestas);

            FilaEnlaceInmovilizadoDTO fila = filas.Single();
            Assert.IsTrue(fila.Ok);
            Assert.AreEqual("1824", fila.Inmovilizado);
            Assert.AreEqual(1242, fila.Registro);
            Assert.AreEqual("A32046", fila.NumeroSerie);
            Assert.AreEqual("CertezaAlta", fila.Origen);
        }

        [TestMethod]
        public void Enlace_ParAMano_AunqueSoloSeParezca()
        {
            (DatosAparatos datos, PropuestasInmovilizadosDTO propuestas) = ParaEnlazar();
            var peticion = new PeticionEnlaceInmovilizadosDTO { Pares = { new ParEnlaceInmovilizadoDTO { Inmovilizado = "I/1825", Registro = 1168 } } };

            FilaEnlaceInmovilizadoDTO fila = ReglaEnlaceInmovilizados.Planificar(peticion, datos, propuestas).Single();

            Assert.IsTrue(fila.Ok, fila.Motivo);
            Assert.AreEqual("AMano", fila.Origen);
            Assert.AreEqual("B1", fila.NumeroSerie);
            Assert.AreEqual("1", fila.Empresa);
        }

        [TestMethod]
        public void Enlace_ParSinRegistro_ElDeLaPropuestaSoloSiEsDeCertezaAlta()
        {
            (DatosAparatos datos, PropuestasInmovilizadosDTO propuestas) = ParaEnlazar();
            var peticion = new PeticionEnlaceInmovilizadosDTO
            {
                Pares = { new ParEnlaceInmovilizadoDTO { Inmovilizado = "1824" }, new ParEnlaceInmovilizadoDTO { Inmovilizado = "1825" } }
            };

            List<FilaEnlaceInmovilizadoDTO> filas = ReglaEnlaceInmovilizados.Planificar(peticion, datos, propuestas);

            Assert.IsTrue(filas[0].Ok);
            Assert.AreEqual(1242, filas[0].Registro);
            Assert.IsFalse(filas[1].Ok);
            StringAssert.Contains(filas[1].Motivo, "Solo se parece");
        }

        [DataTestMethod]
        [DataRow("XYZ", 1242, "no es un número de inmovilizado")]
        [DataRow("9999", 1242, "No existe el inmovilizado I/9999")]
        [DataRow("1826", 1242, "está anulado")]
        [DataRow("1600", 1242, "ya está en el registro 1500")]
        [DataRow("1600", 1500, "no hay nada que hacer")]
        [DataRow("1825", 1500, "ya tiene el inmovilizado I/1600")]
        [DataRow("1825", 7777, "No existe el registro 7777")]
        public void Enlace_ParQueNoSePuede_ConSuMotivo(string inmovilizado, int registro, string motivo)
        {
            (DatosAparatos datos, PropuestasInmovilizadosDTO propuestas) = ParaEnlazar();
            var peticion = new PeticionEnlaceInmovilizadosDTO { Pares = { new ParEnlaceInmovilizadoDTO { Inmovilizado = inmovilizado, Registro = registro } } };

            FilaEnlaceInmovilizadoDTO fila = ReglaEnlaceInmovilizados.Planificar(peticion, datos, propuestas).Single();

            Assert.IsFalse(fila.Ok);
            StringAssert.Contains(fila.Motivo, motivo);
        }

        [TestMethod]
        public void Enlace_ElMismoRegistroDosVeces_LaPrimeraManda()
        {
            (DatosAparatos datos, PropuestasInmovilizadosDTO propuestas) = ParaEnlazar();
            var peticion = new PeticionEnlaceInmovilizadosDTO
            {
                TodasLasDeCertezaAlta = true,
                Pares = { new ParEnlaceInmovilizadoDTO { Inmovilizado = "1825", Registro = 1242 }, new ParEnlaceInmovilizadoDTO { Inmovilizado = "1824" } }
            };

            List<FilaEnlaceInmovilizadoDTO> filas = ReglaEnlaceInmovilizados.Planificar(peticion, datos, propuestas);

            Assert.AreEqual(1, filas.Count(f => f.Ok));
            Assert.IsTrue(filas[0].Ok);
            StringAssert.Contains(filas[1].Motivo, "registro 1242 ya se enlaza");
            StringAssert.Contains(filas[2].Motivo, "I/1824 ya va en otra fila");
        }

        // ---------------------------------------------------------------- el servicio

        private ILectorAparatos lector;
        private IEscritorEnlacesInmovilizado escritor;

        [TestInitialize]
        public void Preparar()
        {
            lector = A.Fake<ILectorAparatos>();
            escritor = A.Fake<IEscritorEnlacesInmovilizado>();
            A.CallTo(() => lector.Leer(A<FiltroAparatos>._)).ReturnsLazily(() => ParaEnlazar().Item1);
            A.CallTo(() => escritor.Enlazar(A<EnlaceInmovilizado>._, A<string>._)).Returns(true);
        }

        [TestMethod]
        public async Task Servicio_Ensayo_NoEscribe()
        {
            var servicio = new ServicioEnlaceInmovilizados(lector, escritor);

            ResultadoEnlaceInmovilizadosDTO r = await servicio.Enlazar(new PeticionEnlaceInmovilizadosDTO { TodasLasDeCertezaAlta = true }, true, "Santiago");

            Assert.IsTrue(r.Ensayo);
            Assert.AreEqual(1, r.Enlazados);
            A.CallTo(() => escritor.Enlazar(A<EnlaceInmovilizado>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => lector.Leer(A<FiltroAparatos>.That.Matches(f => f.Producto == null && !f.SoloListaAlquileres && !f.ConStock))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Servicio_SinEnsayo_EscribeLasOkConElNumeroTalCual()
        {
            var servicio = new ServicioEnlaceInmovilizados(lector, escritor);

            ResultadoEnlaceInmovilizadosDTO r = await servicio.Enlazar(new PeticionEnlaceInmovilizadosDTO { TodasLasDeCertezaAlta = true }, false, @"NUEVAVISION\Santiago");

            Assert.AreEqual(1, r.Enlazados);
            A.CallTo(() => escritor.Enlazar(A<EnlaceInmovilizado>.That.Matches(e => e.Registro == 1242 && e.Inmovilizado == "1824" && e.Producto == "36929"
                && e.NumeroSerie == "A32046" && e.Empresa == "1"), @"NUEVAVISION\Santiago")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Servicio_SiHaCambiadoEntretanto_LaFilaNoQuedaOk()
        {
            A.CallTo(() => escritor.Enlazar(A<EnlaceInmovilizado>._, A<string>._)).Returns(false);
            var servicio = new ServicioEnlaceInmovilizados(lector, escritor);

            ResultadoEnlaceInmovilizadosDTO r = await servicio.Enlazar(new PeticionEnlaceInmovilizadosDTO { TodasLasDeCertezaAlta = true }, false, "Santiago");

            Assert.AreEqual(0, r.Enlazados);
            StringAssert.Contains(r.Filas.Single().Motivo, "han cambiado mientras tanto");
        }

        [TestMethod]
        public async Task Servicio_SinNadaQueEnlazar_ArgumentException()
        {
            var servicio = new ServicioEnlaceInmovilizados(lector, escritor);
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => servicio.Enlazar(new PeticionEnlaceInmovilizadosDTO(), true, "x"));
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => servicio.Enlazar(null, true, "x"));
            var muchos = new PeticionEnlaceInmovilizadosDTO();
            muchos.Pares.AddRange(Enumerable.Range(1, ReglaEnlaceInmovilizados.MAXIMO_PARES + 1).Select(n => new ParEnlaceInmovilizadoDTO { Inmovilizado = n.ToString() }));
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => servicio.Enlazar(muchos, true, "x"));
        }

        // ---------------------------------------------------------------- los descuadres nuevos del informe

        [TestMethod]
        public void Descuadres_RegistroInmovilizadoSinFicha()
        {
            DatosAparatos datos = Datos();
            datos.Registros.Add(R(1, serie: "S1", inmovilizado: "9999"));
            datos.Registros.Add(R(2, serie: "S2", inmovilizado: "1700"));
            datos.Registros.Add(R(3, serie: "S3", inmovilizado: "1701"));
            datos.Inmovilizados.Add(I("1700", "Depiblend n/s S2", estado: -1));
            datos.Inmovilizados.Add(I("1701", "Depiblend n/s S3"));

            InformeDescuadresAparatosDTO informe = ReglaDescuadresAparatos.Calcular(datos, soloListaAlquileres: true);

            DescuadreAparatoDTO s1 = informe.Descuadres.Single(d => d.NumeroSerie == "S1");
            DescuadreAparatoDTO s2 = informe.Descuadres.Single(d => d.NumeroSerie == "S2");
            Assert.IsTrue(s1.Motivos.Contains(MotivoDescuadreAparato.InmovilizadoSinFicha));
            Assert.AreEqual("I/9999", s1.Inmovilizado);
            StringAssert.Contains(s1.TextoMotivo, "no existe");
            StringAssert.Contains(s2.TextoMotivo, "I/1700, que está anulado");
            Assert.IsFalse(informe.Descuadres.Any(d => d.NumeroSerie == "S3"));
        }

        [TestMethod]
        public void Descuadres_FichaConSerieQueNoEsDeNingunAparato_AlFinalYSoloConTodoElAlcance()
        {
            DatosAparatos datos = Datos();
            datos.Registros.Add(R(1934, serie: "A32046"));
            datos.Ventas.Add(new VentaAparatoFila { Pedido = 930489, Producto = "36929", NumeroSerie = "A32046", Cantidad = 1, FechaAlbaran = D2, Cliente = "22709" });
            datos.Inmovilizados.Add(I("1825", "DEPIBLEND ref.36929 n/s ZZZ999"));
            datos.Inmovilizados.Add(I("1824", "MESA n/s 777"));

            InformeDescuadresAparatosDTO todo = ReglaDescuadresAparatos.Calcular(datos, soloListaAlquileres: false);
            InformeDescuadresAparatosDTO lista = ReglaDescuadresAparatos.Calcular(datos, soloListaAlquileres: true);

            List<DescuadreAparatoDTO> sinAparato = todo.Descuadres.Where(d => d.Motivo == MotivoDescuadreAparato.InmovilizadoSinAparato).ToList();
            CollectionAssert.AreEqual(new[] { "I/1824", "I/1825" }, sinAparato.Select(d => d.Inmovilizado).ToList());
            Assert.AreEqual("36929", sinAparato[1].Producto);
            Assert.AreEqual("DEPIBLEND", sinAparato[1].Nombre);
            Assert.AreEqual(MotivoDescuadreAparato.InmovilizadoSinAparato, todo.Descuadres.Last().Motivo);
            Assert.AreEqual(todo.Descuadres.Count, todo.TotalDescuadres);
            Assert.IsFalse(lista.Descuadres.Any(d => d.Motivo == MotivoDescuadreAparato.InmovilizadoSinAparato));
        }

        [TestMethod]
        public void Movimientos_ElInmovilizadoConTextoDetras_CasaConElAparatoPorElPrimerTramo()
        {
            DatosAparatos datos = Datos();
            datos.Registros.Add(R(1242, serie: "A32046"));
            datos.Inmovilizados.Add(I("1824", "DEPIBLEND n/s A32046 demo"));

            MovimientoAparatoDTO inmovilizado = ConstructorMovimientosAparato.Movimientos(datos).Single(m => m.Tipo == TipoMovimientoAparato.Inmovilizado);

            Assert.AreEqual("A32046", inmovilizado.NumeroSerie);
            Assert.AreEqual("36929", inmovilizado.Producto);
        }

        [TestMethod]
        public void Movimientos_ConLaColumna_ElProductoYLaSerieDeLaColumna()
        {
            DatosAparatos datos = Datos();
            datos.Registros.Add(R(1242, producto: "44276", serie: "X1"));
            datos.Inmovilizados.Add(I("1824", "Sin serie escrita", producto: "44276", serie: "X1"));

            MovimientoAparatoDTO inmovilizado = ConstructorMovimientosAparato.Movimientos(datos).Single(m => m.Tipo == TipoMovimientoAparato.Inmovilizado);

            Assert.AreEqual("44276", inmovilizado.Producto);
            Assert.AreEqual("X1", inmovilizado.NumeroSerie);
        }

        // ---------------------------------------------------------------- SQL

        [TestMethod]
        public void Sql_InmovilizadosConYSinColumnas()
        {
            var historia = new FiltroAparatos { Producto = "36929", NumeroSerie = "A32046" };
            string sin = LectorAparatosSql.SqlInmovilizados(new FiltroAparatos());
            string con = LectorAparatosSql.SqlInmovilizados(new FiltroAparatos(), conColumnas: true);

            StringAssert.Contains(sin, "CAST(NULL AS varchar(30)) AS NumeroSerie");
            Assert.IsFalse(sin.Contains("i.NumeroSerie"));
            StringAssert.Contains(con, "RTRIM(i.NumeroSerie) AS NumeroSerie");
            StringAssert.Contains(LectorAparatosSql.SqlInmovilizados(historia, true), "UPPER(LTRIM(RTRIM(i.NumeroSerie))) = @serie");
            StringAssert.Contains(LectorAparatosSql.SQL_COLUMNAS_INMOVILIZADOS, "COL_LENGTH('dbo.Inmovilizados', 'NumeroSerie')");
        }

        [TestMethod]
        public void Sql_ElEnlaceSoloSiNadaHaCambiado()
        {
            string sql = EscritorEnlacesInmovilizadoSql.SQL_ENLAZAR;
            StringAssert.Contains(sql, "UPDATE dbo.CabAlquileres");
            StringAssert.Contains(sql, "(Inmovilizado IS NULL OR LTRIM(RTRIM(Inmovilizado)) = '')");
            StringAssert.Contains(sql, "UPPER(LTRIM(RTRIM(NumeroSerie))) = @Serie");
            StringAssert.Contains(sql, "i.Estado >= 0");
            StringAssert.Contains(sql, "NOT EXISTS (SELECT 1 FROM dbo.CabAlquileres o");
            StringAssert.Contains(EscritorEnlacesInmovilizadoSql.SQL_RASTRO, "dbo.Modificaciones");
            // Sin el script, la columna nueva no aparece fuera del EXEC (el lote compila igual)
            string columnas = EscritorEnlacesInmovilizadoSql.SQL_COLUMNAS_FICHA;
            StringAssert.StartsWith(columnas.Trim(), "IF COL_LENGTH('dbo.Inmovilizados', 'NumeroSerie') IS NOT NULL");
            StringAssert.Contains(columnas, "EXEC sp_executesql N'UPDATE dbo.Inmovilizados");
        }

        // ---------------------------------------------------------------- el controlador

        private AparatosController Controlador(IServicioEnlaceInmovilizados servicio, params Claim[] claims)
        {
            var lista = new List<Claim> { new Claim(ClaimTypes.Name, "Santiago") };
            lista.AddRange(claims);
            return new AparatosController(lector, servicio)
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                User = new ClaimsPrincipal(new ClaimsIdentity(lista, "Bearer"))
            };
        }

        [TestMethod]
        public async Task Controlador_SinGrupo_403YNoLeeNiEscribe()
        {
            IServicioEnlaceInmovilizados servicio = A.Fake<IServicioEnlaceInmovilizados>();

            var propuestas = (NegotiatedContentResult<HttpError>)await Controlador(servicio).GetPropuestasInmovilizados();
            var enlazar = (NegotiatedContentResult<HttpError>)await Controlador(servicio, new Claim("cliente", "15191"), new Claim(ClaimTypes.Role, "Almacén"))
                .PostEnlazarInmovilizados(new PeticionEnlaceInmovilizadosDTO { TodasLasDeCertezaAlta = true });

            Assert.AreEqual(HttpStatusCode.Forbidden, propuestas.StatusCode);
            Assert.AreEqual(HttpStatusCode.Forbidden, enlazar.StatusCode);
            A.CallTo(servicio).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Controlador_Propuestas_Ok()
        {
            var ok = (OkNegotiatedContentResult<PropuestasInmovilizadosDTO>)await Controlador(new ServicioEnlaceInmovilizados(lector, escritor), new Claim(ClaimTypes.Role, "Administración"))
                .GetPropuestasInmovilizados();
            Assert.AreEqual(1, ok.Content.ConCertezaAlta);
        }

        [TestMethod]
        public async Task Controlador_Enlazar_PorDefectoEsEnsayo()
        {
            var ok = (OkNegotiatedContentResult<ResultadoEnlaceInmovilizadosDTO>)await Controlador(new ServicioEnlaceInmovilizados(lector, escritor), new Claim(ClaimTypes.Role, "Almacén"))
                .PostEnlazarInmovilizados(new PeticionEnlaceInmovilizadosDTO { TodasLasDeCertezaAlta = true });

            Assert.IsTrue(ok.Content.Ensayo);
            A.CallTo(() => escritor.Enlazar(A<EnlaceInmovilizado>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Controlador_Enlazar_SinEnsayoConElUsuarioDeAuditoria()
        {
            await Controlador(new ServicioEnlaceInmovilizados(lector, escritor), new Claim(ClaimTypes.Role, "Dirección"))
                .PostEnlazarInmovilizados(new PeticionEnlaceInmovilizadosDTO { TodasLasDeCertezaAlta = true }, ensayo: false);

            A.CallTo(() => escritor.Enlazar(A<EnlaceInmovilizado>._, A<string>.That.Contains("Santiago"))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Controlador_Enlazar_SinNada_400()
        {
            var r = (NegotiatedContentResult<HttpError>)await Controlador(new ServicioEnlaceInmovilizados(lector, escritor), new Claim(ClaimTypes.Role, "Compras"))
                .PostEnlazarInmovilizados(new PeticionEnlaceInmovilizadosDTO());
            Assert.AreEqual(HttpStatusCode.BadRequest, r.StatusCode);
        }
    }
}
