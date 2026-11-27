using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Clientes
{
    /// <summary>
    /// Carlos 27/11/26 (NestoAPI#876): enlazar fichas de cliente equivalentes (duplicado o cambio de forma fiscal) por la API, ver el
    /// grupo y deshacerlo; y que el extracto y los rapports las incluyan solo si se pide.
    /// </summary>
    [TestClass]
    public class EquivalenciasClientesTests
    {
        private static DatosFichaEquivalente Ficha(string cliente, string nif = "12345678Z", bool activa = true, string vendedor = "JE")
            => new DatosFichaEquivalente { Cliente = cliente, Nombre = "Ficha " + cliente, Estado = (short)(activa ? 0 : -1), Activa = activa, Nif = nif, Vendedor = vendedor };

        private static Dictionary<string, string> Enlaces(params string[] pares)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < pares.Length; i += 2)
            {
                d[pares[i]] = pares[i + 1];
            }
            return d;
        }

        private static DecisionEnlaceEquivalente Decidir(string cliente, string anterior, Dictionary<string, string> enlaces, string motivo = "Duplicado",
            DatosFichaEquivalente fichaCliente = null, DatosFichaEquivalente fichaAnterior = null)
            => ReglaEnlaceEquivalentes.Decidir(cliente, anterior, motivo, fichaCliente ?? Ficha(cliente), fichaAnterior ?? Ficha(anterior, activa: false), enlaces);

        // ----- LA REGLA del enlace -----

        [TestMethod]
        public void Decidir_FaltaAlguno_400()
        {
            Assert.AreEqual(400, ReglaEnlaceEquivalentes.Decidir(" ", "8244", "Duplicado", Ficha("x"), Ficha("8244"), null).Estado);
            Assert.AreEqual(400, ReglaEnlaceEquivalentes.Decidir("26815", null, "Duplicado", Ficha("26815"), Ficha("x"), null).Estado);
        }

        [TestMethod]
        public void Decidir_ElMismo_400()
        {
            DecisionEnlaceEquivalente d = Decidir("26815", " 26815 ", Enlaces());

            Assert.AreEqual(400, d.Estado);
            StringAssert.Contains(d.Motivo, "misma");
        }

        [TestMethod]
        public void Decidir_MotivoQueNoEsNinguno_400()
        {
            Assert.AreEqual(400, Decidir("26815", "8244", Enlaces(), motivo: "Otro").Estado);
            Assert.AreEqual(400, Decidir("26815", "8244", Enlaces(), motivo: null).Estado);
        }

        [TestMethod]
        public void NormalizarMotivo_ToleraMayusculasEspaciosYLaFormaLarga()
        {
            Assert.AreEqual(ReglaEnlaceEquivalentes.DUPLICADO, ReglaEnlaceEquivalentes.NormalizarMotivo(" duplicado "));
            Assert.AreEqual(ReglaEnlaceEquivalentes.CAMBIO_FISCAL, ReglaEnlaceEquivalentes.NormalizarMotivo("Cambio fiscal"));
            Assert.AreEqual(ReglaEnlaceEquivalentes.CAMBIO_FISCAL, ReglaEnlaceEquivalentes.NormalizarMotivo("CAMBIO-FISCAL"));
            Assert.IsNull(ReglaEnlaceEquivalentes.NormalizarMotivo("Fusión"));
        }

        [TestMethod]
        public void Decidir_NoExisteAlguno_404()
        {
            Assert.AreEqual(404, ReglaEnlaceEquivalentes.Decidir("26815", "8244", "Duplicado", null, Ficha("8244"), Enlaces()).Estado);
            Assert.AreEqual(404, ReglaEnlaceEquivalentes.Decidir("26815", "8244", "Duplicado", Ficha("26815"), null, Enlaces()).Estado);
        }

        [TestMethod]
        public void Decidir_SinEnlaces_EnlazaDirecto()
        {
            DecisionEnlaceEquivalente d = Decidir(" 26815 ", "8244 ", Enlaces());

            Assert.AreEqual(200, d.Estado);
            Assert.IsFalse(d.YaEstaba);
            Assert.AreEqual("26815", d.Cliente);
            Assert.AreEqual("8244", d.ClienteAnterior);
            Assert.IsFalse(d.Encadenado);
            Assert.AreEqual(ReglaEnlaceEquivalentes.DUPLICADO, d.MotivoEnlace);
            Assert.AreEqual(0, d.Avisos.Count, "Mismo NIF y el anterior de baja: nada que avisar");
        }

        [TestMethod]
        public void Decidir_YaEnlazadoElMismoPar_200SinEscribir()
        {
            DecisionEnlaceEquivalente d = Decidir("26815", "8244", Enlaces("26815", "8244"));

            Assert.AreEqual(200, d.Estado);
            Assert.IsTrue(d.YaEstaba);
            Assert.IsNull(d.Cliente, "No hay nada que escribir");
            StringAssert.Contains(d.Mensaje, "ya tenía como anterior");
        }

        [TestMethod]
        public void Decidir_YaEquivalentesPorLaCadena_200SinEscribir()
        {
            DecisionEnlaceEquivalente d = Decidir("39570", "5868", Enlaces("39570", "14315", "14315", "5868"));

            Assert.IsTrue(d.YaEstaba);
            StringAssert.Contains(d.Mensaje, "39570 → 14315 → 5868");
        }

        [TestMethod]
        public void Decidir_AlReves_409Ciclo()
        {
            DecisionEnlaceEquivalente d = Decidir("8244", "26815", Enlaces("26815", "8244"));

            Assert.AreEqual(409, d.Estado);
            Assert.AreEqual(ReglaEnlaceEquivalentes.CODIGO_CICLO, d.Codigo);
        }

        [TestMethod]
        public void Decidir_LasDosLleganALaMisma_409Ciclo()
        {
            // A → X y P → X: enlazar A con P dejaría X → P → X
            DecisionEnlaceEquivalente d = Decidir("A", "P", Enlaces("A", "X", "P", "X"));

            Assert.AreEqual(409, d.Estado);
            Assert.AreEqual(ReglaEnlaceEquivalentes.CODIGO_CICLO, d.Codigo);
        }

        [TestMethod]
        public void Decidir_ConCadenaQueNoTermina_409()
        {
            DecisionEnlaceEquivalente d = Decidir("A", "Z", Enlaces("A", "B", "B", "A"));

            Assert.AreEqual(409, d.Estado);
        }

        [TestMethod]
        public void Decidir_TresFichas_EncadenaComoElScriptDel26()
        {
            // 39570 ya apunta a 14315; se pide 39570 → 5868: se enlaza 14315 → 5868
            DecisionEnlaceEquivalente d = Decidir("39570", "5868", Enlaces("39570", "14315"));

            Assert.AreEqual(200, d.Estado);
            Assert.IsTrue(d.Encadenado);
            Assert.AreEqual("14315", d.Cliente);
            Assert.AreEqual("5868", d.ClienteAnterior);
            StringAssert.Contains(d.Mensaje, "se encadena");
        }

        [TestMethod]
        public void Decidir_DuplicadoConNifDistinto_AvisaSinImpedir()
        {
            DecisionEnlaceEquivalente d = Decidir("26815", "8244", Enlaces(), fichaCliente: Ficha("26815", "12345678-Z"), fichaAnterior: Ficha("8244", "B12345678", activa: false));

            Assert.AreEqual(200, d.Estado);
            Assert.AreEqual("26815", d.Cliente);
            Assert.AreEqual(1, d.Avisos.Count);
            StringAssert.Contains(d.Avisos[0], "Los NIF no coinciden");
        }

        [TestMethod]
        public void Decidir_DuplicadoConElMismoNifEscritoDistinto_NoAvisa()
        {
            DecisionEnlaceEquivalente d = Decidir("26815", "8244", Enlaces(), fichaCliente: Ficha("26815", "12345678-z"), fichaAnterior: Ficha("8244", " 12345678Z", activa: false));

            Assert.AreEqual(0, d.Avisos.Count);
        }

        [TestMethod]
        public void Decidir_DuplicadoConLaOtraAunDeAlta_Avisa()
        {
            DecisionEnlaceEquivalente d = Decidir("26815", "8244", Enlaces(), fichaAnterior: Ficha("8244", activa: true));

            Assert.AreEqual(200, d.Estado);
            Assert.IsTrue(d.Avisos.Any(a => a.Contains("sigue de alta")));
        }

        [TestMethod]
        public void Decidir_CambioFiscalConElMismoNif_Avisa()
        {
            DecisionEnlaceEquivalente d = Decidir("42178", "37283", Enlaces(), motivo: "CambioFiscal");

            Assert.AreEqual(ReglaEnlaceEquivalentes.CAMBIO_FISCAL, d.MotivoEnlace);
            Assert.IsTrue(d.Avisos.Any(a => a.Contains("mismo NIF")));
        }

        [TestMethod]
        public void Decidir_LaOtraYaEraAnteriorDeOtraFicha_AvisaDeLaRama()
        {
            DecisionEnlaceEquivalente d = Decidir("D", "B", Enlaces("A", "B"));

            Assert.AreEqual(200, d.Estado);
            Assert.AreEqual("D", d.Cliente);
            Assert.IsTrue(d.Avisos.Any(a => a.Contains("ya era el anterior del A")));
        }

        [TestMethod]
        public void MotivoNoSePuedeDeshacer_SoloElEnlaceExacto()
        {
            Dictionary<string, string> enlaces = Enlaces("26815", "8244");

            Assert.IsNull(ReglaEnlaceEquivalentes.MotivoNoSePuedeDeshacer(" 26815", "8244 ", enlaces));
            Assert.IsNotNull(ReglaEnlaceEquivalentes.MotivoNoSePuedeDeshacer("26815", "9999", enlaces));
            Assert.IsNotNull(ReglaEnlaceEquivalentes.MotivoNoSePuedeDeshacer("8244", "26815", enlaces));
        }

        // ----- El grupo entero (los dos sentidos) -----

        [TestMethod]
        public void Grupo_DesdeCualquieraDeLasTres_SalenTodas()
        {
            Dictionary<string, string> enlaces = Enlaces("39570", "14315", "14315", "5868", "999", "111");

            CollectionAssert.AreEqual(new[] { "39570", "14315", "5868" }, ReglaClientesEquivalentes.Grupo("39570", enlaces).ToList());
            CollectionAssert.AreEqual(new[] { "5868", "39570", "14315" }, ReglaClientesEquivalentes.Grupo(" 5868 ", enlaces).ToList());
            CollectionAssert.AreEqual(new[] { "14315", "39570", "5868" }, ReglaClientesEquivalentes.Grupo("14315", enlaces).ToList());
            CollectionAssert.AreEqual(new[] { "777" }, ReglaClientesEquivalentes.Grupo("777", enlaces).ToList());
            Assert.AreEqual(0, ReglaClientesEquivalentes.Grupo(" ", enlaces).Count);
        }

        [TestMethod]
        public void Grupo_ConRama_SalenLasDosRamas()
        {
            // A → B y D → B
            CollectionAssert.AreEquivalent(new[] { "A", "B", "D" }, ReglaClientesEquivalentes.Grupo("A", Enlaces("A", "B", "D", "B")).ToList());
            Assert.AreEqual("B", ReglaClientesEquivalentes.Grupo("B", Enlaces("A", "B", "D", "B"))[0]);
        }

        [TestMethod]
        public void Grupo_ConUnCicloEnLosDatos_NoSeCuelga()
        {
            CollectionAssert.AreEquivalent(new[] { "A", "B", "C" }, ReglaClientesEquivalentes.Grupo("A", Enlaces("A", "B", "B", "C", "C", "A")).ToList());
        }

        [TestMethod]
        public async Task ClientesEquivalentes_Todas_LeeLosDosSentidos()
        {
            ClientesEquivalentes.Invalidar();
            IRepositorioClientesAnteriores repo = A.Fake<IRepositorioClientesAnteriores>();
            A.CallTo(() => repo.TablaExiste()).Returns(true);
            A.CallTo(() => repo.LeerEnlaces("1")).Returns(new List<EnlaceClienteAnterior>
            {
                new EnlaceClienteAnterior { Cliente = "26815     ", ClienteAnterior = "8244      ", Fecha = new DateTime(2026, 11, 26) }
            });
            try
            {
                CollectionAssert.AreEqual(new[] { "8244", "26815" }, (await new ClientesEquivalentes(repo).Todas("1", "8244")).ToList());
                CollectionAssert.AreEqual(new[] { "8244" }, (await new ClientesEquivalentes(repo).De("1", "8244")).ToList(), "De() sigue siendo solo hacia atrás");
            }
            finally
            {
                ClientesEquivalentes.Invalidar();
            }
        }

        // ----- Lo que miran el extracto y los rapports -----

        [TestMethod]
        public void ConsultaConEquivalentes_SinPedirlo_SoloElClienteYNiSeMira()
        {
            IClientesEquivalentes equivalentes = A.Fake<IClientesEquivalentes>();

            IReadOnlyList<string> r = ConsultaConEquivalentes.Clientes(equivalentes, "1", "26815", false);

            CollectionAssert.AreEqual(new[] { "26815" }, r.ToList());
            A.CallTo(() => equivalentes.Todas(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void ConsultaConEquivalentes_Pedido_TodoElGrupo()
        {
            IClientesEquivalentes equivalentes = A.Fake<IClientesEquivalentes>();
            A.CallTo(() => equivalentes.Todas("1", "26815")).Returns(Task.FromResult<IReadOnlyList<string>>(new List<string> { "26815", "8244" }));

            IReadOnlyList<string> r = ConsultaConEquivalentes.Clientes(equivalentes, "1", " 26815 ", true);

            CollectionAssert.AreEqual(new[] { "26815", "8244" }, r.ToList());
            CollectionAssert.AreEqual(new[] { "8244" }, ConsultaConEquivalentes.Otras(r, "26815 "));
        }

        [TestMethod]
        public void ConsultaConEquivalentes_SiFalla_SoloElCliente()
        {
            IClientesEquivalentes equivalentes = A.Fake<IClientesEquivalentes>();
            A.CallTo(() => equivalentes.Todas(A<string>._, A<string>._)).Throws(new InvalidOperationException("sin BD"));

            CollectionAssert.AreEqual(new[] { "26815" }, ConsultaConEquivalentes.Clientes(equivalentes, "1", "26815", true).ToList());
        }

        [TestMethod]
        public void Rapports_DelCliente_SinEquivalentesElFiltroDeSiempre_ConEllosTodosSusContactos()
        {
            IQueryable<SeguimientoCliente> rapports = new List<SeguimientoCliente>
            {
                new SeguimientoCliente { NºOrden = 1, Empresa = "1", Número = "26815", Contacto = "0" },
                new SeguimientoCliente { NºOrden = 2, Empresa = "1", Número = "26815", Contacto = "1" },
                new SeguimientoCliente { NºOrden = 3, Empresa = "1", Número = "8244", Contacto = "0" },
                new SeguimientoCliente { NºOrden = 4, Empresa = "1", Número = "8244", Contacto = "3" },
                new SeguimientoCliente { NºOrden = 5, Empresa = "3", Número = "8244", Contacto = "0" },
                new SeguimientoCliente { NºOrden = 6, Empresa = "1", Número = "11111", Contacto = "0" }
            }.AsQueryable();

            CollectionAssert.AreEqual(new[] { 1 }, SeguimientosClientesController.DelCliente(rapports, "1", "26815", "0", new List<string>())
                .Select(s => s.NºOrden).ToList());
            CollectionAssert.AreEqual(new[] { 1, 3, 4 }, SeguimientosClientesController.DelCliente(rapports, "1", "26815", "0", new List<string> { "8244" })
                .Select(s => s.NºOrden).OrderBy(n => n).ToList());
        }

        // ----- El servicio -----

        private static IRepositorioEquivalenciasClientes Repo(List<EnlaceEquivalenteCliente> enlaces, params DatosFichaEquivalente[] fichas)
        {
            IRepositorioEquivalenciasClientes repo = A.Fake<IRepositorioEquivalenciasClientes>();
            A.CallTo(() => repo.TablaExiste()).Returns(true);
            A.CallTo(() => repo.LeerEnlaces(A<string>._)).ReturnsLazily(() => Task.FromResult(enlaces.ToList()));
            A.CallTo(() => repo.LeerFichas(A<string>._, A<IEnumerable<string>>._)).ReturnsLazily((string e, IEnumerable<string> cs)
                => Task.FromResult(fichas.Where(f => cs.Contains(f.Cliente, StringComparer.OrdinalIgnoreCase)).ToList()));
            A.CallTo(() => repo.Enlazar(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._))
                .ReturnsLazily((string e, string c, string a, string m, string co, string u, string r) =>
                {
                    enlaces.Add(new EnlaceEquivalenteCliente { Cliente = c, ClienteAnterior = a, Motivo = m, Comentario = co, Usuario = u, Fecha = new DateTime(2026, 11, 27) });
                    return Task.FromResult(true);
                });
            A.CallTo(() => repo.Deshacer(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._))
                .ReturnsLazily((string e, string c, string a, string u, string r) => Task.FromResult(enlaces.RemoveAll(x => x.Cliente == c && x.ClienteAnterior == a) == 1));
            return repo;
        }

        private static Func<string, Task<string>> ConPermiso => v => Task.FromResult<string>(null);

        [TestMethod]
        public async Task Servicio_Enlazar_EscribeConElMotivoYElRastro()
        {
            var enlaces = new List<EnlaceEquivalenteCliente>();
            IRepositorioEquivalenciasClientes repo = Repo(enlaces, Ficha("26815"), Ficha("8244", activa: false));

            ResultadoEquivalencias<ResultadoEnlaceEquivalenteDTO> r = await new ServicioEquivalenciasClientes(repo).Enlazar(new PeticionEnlaceEquivalenteDTO
            {
                Cliente = "26815",
                ClienteAnterior = "8244",
                Motivo = "duplicado",
                Comentario = "  Mismo NIF, alta repetida  "
            }, "Carlos", ConPermiso);

            Assert.AreEqual(200, r.Estado);
            Assert.IsTrue(r.Datos.Cambiado);
            Assert.AreEqual("1", r.Datos.Empresa);
            A.CallTo(() => repo.Enlazar("1", "26815", "8244", "Duplicado", "Mismo NIF, alta repetida", "Carlos", "26815: 8244 (Duplicado; Mismo NIF, alta repetida)"))
                .MustHaveHappenedOnceExactly();
            CollectionAssert.AreEqual(new[] { "26815", "8244" }, r.Datos.Fichas.Select(f => f.Cliente).ToList());
            Assert.AreEqual("8244", r.Datos.Fichas[0].ClienteAnterior);
            Assert.AreEqual("Duplicado", r.Datos.Fichas[0].Motivo);
            Assert.IsTrue(r.Datos.Fichas[0].EsLaConsultada);
        }

        [TestMethod]
        public async Task Servicio_Enlazar_DosVeces_LaSegundaNoEscribe()
        {
            var enlaces = new List<EnlaceEquivalenteCliente> { new EnlaceEquivalenteCliente { Cliente = "26815", ClienteAnterior = "8244", Fecha = DateTime.Today } };
            IRepositorioEquivalenciasClientes repo = Repo(enlaces, Ficha("26815"), Ficha("8244", activa: false));

            ResultadoEquivalencias<ResultadoEnlaceEquivalenteDTO> r = await new ServicioEquivalenciasClientes(repo)
                .Enlazar(new PeticionEnlaceEquivalenteDTO { Cliente = "26815", ClienteAnterior = "8244", Motivo = "Duplicado" }, "Carlos", ConPermiso);

            Assert.AreEqual(200, r.Estado);
            Assert.IsTrue(r.Datos.YaEstaba);
            Assert.IsFalse(r.Datos.Cambiado);
            A.CallTo(() => repo.Enlazar(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Servicio_Enlazar_SinPermiso_403SinEscribir()
        {
            IRepositorioEquivalenciasClientes repo = Repo(new List<EnlaceEquivalenteCliente>(), Ficha("26815"), Ficha("8244", vendedor: "PA"));
            string vendedorVisto = null;

            ResultadoEquivalencias<ResultadoEnlaceEquivalenteDTO> r = await new ServicioEquivalenciasClientes(repo)
                .Enlazar(new PeticionEnlaceEquivalenteDTO { Cliente = "26815", ClienteAnterior = "8244", Motivo = "Duplicado" }, "Pepe",
                    v => { vendedorVisto = v; return Task.FromResult("no"); });

            Assert.AreEqual(403, r.Estado);
            Assert.AreEqual("PA", vendedorVisto, "Se mira el vendedor de la otra ficha, como en #876");
            A.CallTo(() => repo.Enlazar(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Servicio_Enlazar_SinTabla_503()
        {
            IRepositorioEquivalenciasClientes repo = A.Fake<IRepositorioEquivalenciasClientes>();
            A.CallTo(() => repo.TablaExiste()).Returns(false);

            ResultadoEquivalencias<ResultadoEnlaceEquivalenteDTO> r = await new ServicioEquivalenciasClientes(repo)
                .Enlazar(new PeticionEnlaceEquivalenteDTO { Cliente = "26815", ClienteAnterior = "8244", Motivo = "Duplicado" }, "Carlos", ConPermiso);

            Assert.AreEqual(503, r.Estado);
        }

        [TestMethod]
        public async Task Servicio_Enlazar_OtroLoEnlazaALaVez_409Ocupado()
        {
            IRepositorioEquivalenciasClientes repo = Repo(new List<EnlaceEquivalenteCliente>(), Ficha("26815"), Ficha("8244", activa: false));
            A.CallTo(() => repo.Enlazar(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._)).Returns(false);

            ResultadoEquivalencias<ResultadoEnlaceEquivalenteDTO> r = await new ServicioEquivalenciasClientes(repo)
                .Enlazar(new PeticionEnlaceEquivalenteDTO { Cliente = "26815", ClienteAnterior = "8244", Motivo = "Duplicado" }, "Carlos", ConPermiso);

            Assert.AreEqual(409, r.Estado);
            Assert.AreEqual(ReglaEnlaceEquivalentes.CODIGO_OCUPADO, r.Codigo);
        }

        [TestMethod]
        public async Task Servicio_Enlazar_TresFichas_EncadenaYDevuelveElGrupo()
        {
            var enlaces = new List<EnlaceEquivalenteCliente> { new EnlaceEquivalenteCliente { Cliente = "39570", ClienteAnterior = "14315", Fecha = DateTime.Today } };
            IRepositorioEquivalenciasClientes repo = Repo(enlaces, Ficha("39570"), Ficha("14315", activa: false), Ficha("5868", activa: false));

            ResultadoEquivalencias<ResultadoEnlaceEquivalenteDTO> r = await new ServicioEquivalenciasClientes(repo)
                .Enlazar(new PeticionEnlaceEquivalenteDTO { Cliente = "39570", ClienteAnterior = "5868", Motivo = "Duplicado" }, "Carlos", ConPermiso);

            Assert.AreEqual(200, r.Estado);
            Assert.IsTrue(r.Datos.Encadenado);
            Assert.AreEqual("14315", r.Datos.EnlaceCliente);
            Assert.AreEqual("5868", r.Datos.EnlaceClienteAnterior);
            CollectionAssert.AreEqual(new[] { "39570", "14315", "5868" }, r.Datos.Fichas.Select(f => f.Cliente).ToList());
        }

        [TestMethod]
        public async Task Servicio_Enlazar_SinMotivo_400SinLeerNada()
        {
            IRepositorioEquivalenciasClientes repo = A.Fake<IRepositorioEquivalenciasClientes>();

            ResultadoEquivalencias<ResultadoEnlaceEquivalenteDTO> r = await new ServicioEquivalenciasClientes(repo)
                .Enlazar(new PeticionEnlaceEquivalenteDTO { Cliente = "26815", ClienteAnterior = "8244" }, "Carlos", ConPermiso);

            Assert.AreEqual(400, r.Estado);
            A.CallTo(() => repo.LeerFichas(A<string>._, A<IEnumerable<string>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Servicio_Leer_ElGrupoDesdeLaFichaVieja()
        {
            var enlaces = new List<EnlaceEquivalenteCliente>
            {
                new EnlaceEquivalenteCliente { Cliente = "26815", ClienteAnterior = "8244", Fecha = new DateTime(2026, 11, 26), Usuario = "Duplicado 26/11/26", Motivo = "Duplicado" }
            };
            IRepositorioEquivalenciasClientes repo = Repo(enlaces, Ficha("26815", "1A"), Ficha("8244", "1A", activa: false));

            ResultadoEquivalencias<ClientesEquivalentesDTO> r = await new ServicioEquivalenciasClientes(repo).Leer(null, " 8244 ");

            Assert.AreEqual(200, r.Estado);
            CollectionAssert.AreEqual(new[] { "8244", "26815" }, r.Datos.Fichas.Select(f => f.Cliente).ToList());
            Assert.IsTrue(r.Datos.Fichas[0].EsLaConsultada);
            Assert.IsNull(r.Datos.Fichas[0].ClienteAnterior);
            Assert.IsFalse(r.Datos.Fichas[0].Activa);
            Assert.AreEqual("8244", r.Datos.Fichas[1].ClienteAnterior);
            Assert.AreEqual("Duplicado 26/11/26", r.Datos.Fichas[1].Usuario);
            Assert.AreEqual(new DateTime(2026, 11, 26), r.Datos.Fichas[1].Fecha);
        }

        [TestMethod]
        public async Task Servicio_Leer_NoExiste_404()
        {
            IRepositorioEquivalenciasClientes repo = Repo(new List<EnlaceEquivalenteCliente>());

            Assert.AreEqual(404, (await new ServicioEquivalenciasClientes(repo).Leer("1", "99999")).Estado);
        }

        [TestMethod]
        public async Task Servicio_Leer_SinTabla_SoloLaFicha()
        {
            IRepositorioEquivalenciasClientes repo = Repo(new List<EnlaceEquivalenteCliente>(), Ficha("26815"));
            A.CallTo(() => repo.TablaExiste()).Returns(false);

            ResultadoEquivalencias<ClientesEquivalentesDTO> r = await new ServicioEquivalenciasClientes(repo).Leer("1", "26815");

            Assert.AreEqual(200, r.Estado);
            Assert.AreEqual(1, r.Datos.Fichas.Count);
            A.CallTo(() => repo.LeerEnlaces(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Servicio_Deshacer_SoloElEnlaceExactoYConRastro()
        {
            var enlaces = new List<EnlaceEquivalenteCliente>
            {
                new EnlaceEquivalenteCliente { Cliente = "26815", ClienteAnterior = "8244", Fecha = DateTime.Today, Motivo = "Duplicado" }
            };
            IRepositorioEquivalenciasClientes repo = Repo(enlaces, Ficha("26815"), Ficha("8244", activa: false));
            var servicio = new ServicioEquivalenciasClientes(repo);

            Assert.AreEqual(404, (await servicio.Deshacer("1", "8244", "26815", "Carlos", ConPermiso)).Estado, "Al revés no es ese enlace");
            ResultadoEquivalencias<ResultadoEnlaceEquivalenteDTO> r = await servicio.Deshacer("1", "26815", "8244", "Carlos", ConPermiso);

            Assert.AreEqual(200, r.Estado);
            Assert.IsTrue(r.Datos.Cambiado);
            A.CallTo(() => repo.Deshacer("1", "26815", "8244", "Carlos", "26815: 8244 (Duplicado)")).MustHaveHappenedOnceExactly();
            Assert.AreEqual(1, r.Datos.Fichas.Count, "Ya no tiene equivalentes");
        }

        [TestMethod]
        public async Task Servicio_Deshacer_SinPermiso_403()
        {
            var enlaces = new List<EnlaceEquivalenteCliente> { new EnlaceEquivalenteCliente { Cliente = "26815", ClienteAnterior = "8244", Fecha = DateTime.Today } };
            IRepositorioEquivalenciasClientes repo = Repo(enlaces, Ficha("26815"), Ficha("8244"));

            ResultadoEquivalencias<ResultadoEnlaceEquivalenteDTO> r = await new ServicioEquivalenciasClientes(repo)
                .Deshacer("1", "26815", "8244", "Pepe", v => Task.FromResult("no"));

            Assert.AreEqual(403, r.Estado);
            A.CallTo(() => repo.Deshacer(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        // ----- Los endpoints -----

        private static ClientesController Controlador(IRepositorioEquivalenciasClientes repo, params Claim[] claims)
        {
            var todas = new List<Claim> { new Claim(ClaimTypes.Name, "NUEVAVISION\\Pepe") };
            todas.AddRange(claims);
            return new ClientesController(A.Fake<IGestorClientes>(), A.Fake<IServicioVendedores>())
            {
                RepositorioEquivalenciasClientes = repo,
                User = new ClaimsPrincipal(new ClaimsIdentity(todas, "prueba"))
            };
        }

        [TestMethod]
        public async Task Endpoints_ClienteDeLaTienda_403()
        {
            IRepositorioEquivalenciasClientes repo = A.Fake<IRepositorioEquivalenciasClientes>();
            ClientesController controller = Controlador(repo, new Claim("cliente", "26815"));

            Assert.AreEqual(HttpStatusCode.Forbidden, ((StatusCodeResult)await controller.PostClientesEquivalentes(
                new PeticionEnlaceEquivalenteDTO { Cliente = "26815", ClienteAnterior = "8244", Motivo = "Duplicado" })).StatusCode);
            Assert.AreEqual(HttpStatusCode.Forbidden, ((StatusCodeResult)await controller.GetClientesEquivalentes("1", "26815")).StatusCode);
            Assert.AreEqual(HttpStatusCode.Forbidden, ((StatusCodeResult)await controller.DeleteClientesEquivalentes("1", "26815", "8244")).StatusCode);
            A.CallTo(repo).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Post_Administracion_200ConElUsuarioDelToken()
        {
            var enlaces = new List<EnlaceEquivalenteCliente>();
            IRepositorioEquivalenciasClientes repo = Repo(enlaces, Ficha("26815"), Ficha("8244", activa: false, vendedor: "PA"));

            IHttpActionResult r = await Controlador(repo, new Claim(ClaimTypes.Role, "NUEVAVISION\\Administración"))
                .PostClientesEquivalentes(new PeticionEnlaceEquivalenteDTO { Empresa = "1", Cliente = "26815", ClienteAnterior = "8244", Motivo = "Duplicado" });

            var ok = r as OkNegotiatedContentResult<ResultadoEnlaceEquivalenteDTO>;
            Assert.IsNotNull(ok);
            Assert.IsTrue(ok.Content.Cambiado);
            A.CallTo(() => repo.Enlazar("1", "26815", "8244", "Duplicado", null, A<string>.That.Matches(u => u != null && u.IndexOf("pepe", StringComparison.OrdinalIgnoreCase) >= 0), A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Post_VendedorDeOtraFicha_403()
        {
            IRepositorioEquivalenciasClientes repo = Repo(new List<EnlaceEquivalenteCliente>(), Ficha("26815"), Ficha("8244", vendedor: "PA"));

            IHttpActionResult r = await Controlador(repo, new Claim("Vendedor", "JE"))
                .PostClientesEquivalentes(new PeticionEnlaceEquivalenteDTO { Cliente = "26815", ClienteAnterior = "8244", Motivo = "Duplicado" });

            Assert.AreEqual(HttpStatusCode.Forbidden, EstadoDe(r));
        }

        [TestMethod]
        public async Task Post_ElVendedorDeLaOtraFicha_Puede()
        {
            IRepositorioEquivalenciasClientes repo = Repo(new List<EnlaceEquivalenteCliente>(), Ficha("26815"), Ficha("8244", vendedor: "PA"));

            IHttpActionResult r = await Controlador(repo, new Claim("Vendedor", "PA"))
                .PostClientesEquivalentes(new PeticionEnlaceEquivalenteDTO { Cliente = "26815", ClienteAnterior = "8244", Motivo = "Duplicado" });

            Assert.IsInstanceOfType(r, typeof(OkNegotiatedContentResult<ResultadoEnlaceEquivalenteDTO>));
        }

        [TestMethod]
        public async Task Post_AlReves_409ConCodigo()
        {
            var enlaces = new List<EnlaceEquivalenteCliente> { new EnlaceEquivalenteCliente { Cliente = "26815", ClienteAnterior = "8244", Fecha = DateTime.Today } };
            IRepositorioEquivalenciasClientes repo = Repo(enlaces, Ficha("26815"), Ficha("8244"));

            IHttpActionResult r = await Controlador(repo, new Claim(ClaimTypes.Role, "NUEVAVISION\\Informática"))
                .PostClientesEquivalentes(new PeticionEnlaceEquivalenteDTO { Cliente = "8244", ClienteAnterior = "26815", Motivo = "Duplicado" });

            Assert.AreEqual(HttpStatusCode.Conflict, EstadoDe(r));
        }

        [TestMethod]
        public async Task Get_CualquierUsuarioDeDentro_200()
        {
            IRepositorioEquivalenciasClientes repo = Repo(new List<EnlaceEquivalenteCliente>(), Ficha("26815"));

            IHttpActionResult r = await Controlador(repo, new Claim("Vendedor", "JE")).GetClientesEquivalentes("1", "26815");

            Assert.IsInstanceOfType(r, typeof(OkNegotiatedContentResult<ClientesEquivalentesDTO>));
        }

        private static HttpStatusCode EstadoDe(IHttpActionResult r)
        {
            System.Reflection.PropertyInfo propiedad = r.GetType().GetProperty("StatusCode");
            return propiedad == null ? HttpStatusCode.OK : (HttpStatusCode)propiedad.GetValue(r);
        }
    }
}
