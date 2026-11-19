using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Models.Sincronizacion;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Infraestructure.Sincronizacion;
using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Clientes
{
    /// <summary>
    /// NestoAPI#874 corte 2 (19/11/26): la Dirección 2 al MODIFICAR un cliente. GetClienteCrear la devuelve descompuesta en «¿Qué es?» y
    /// sus campos (regla inversa de Componer); el PUT la guarda con la MISMA regla que el alta, la exige solo si cambia la dirección (y con
    /// ExigirDireccion2Alta = 1) y, sin campos (NestoApp, Nesto anterior), no la toca.
    /// </summary>
    [TestClass]
    public class Direccion2ModificarTests
    {
        // ---- Regla inversa ----

        [DataTestMethod]
        [DataRow("Piso", "1º D", "Piso")]
        [DataRow("Piso", "BAJO IZQ", "Piso")]
        [DataRow("Local", "LOCAL 6 · RÓTULO: EL EDÉN", "Local")]
        [DataRow("Local", "LOCAL · RÓTULO: MARCO ALDANY", "Local")]
        [DataRow("Casa", "CASA", "Casa")]
        [DataRow("Nave", "NAVE 4", "Nave")]
        [DataRow("Otro", "CENTRO COMERCIAL, PLANTA 2", "Otro")]
        [DataRow(null, "1º D", "Otro")]
        [DataRow("Piso", "ÁTICO", "Otro")]
        [DataRow("Local", "LOCAL 3", "Otro")]
        public void Descomponer_YVolverAComponer_DaLoMismo(string tipo, string guardada, string tipoEsperado)
        {
            CamposDireccion2 campos = Direccion2Cliente.Descomponer(tipo, guardada);

            Assert.AreEqual(tipoEsperado, campos.TipoLugar);
            Assert.AreEqual(guardada, campos.Direccion2);
            ResultadoDireccion2 vuelta = Direccion2Cliente.Componer(campos.TipoLugar, campos.Piso, campos.Puerta, campos.NumeroLocal, campos.Rotulo,
                campos.NumeroNave, campos.OtroTexto);
            Assert.IsTrue(vuelta.Valida);
            Assert.AreEqual(guardada, vuelta.Direccion2);
        }

        [TestMethod]
        public void Descomponer_LosCamposDeCadaTipo()
        {
            CamposDireccion2 local = Direccion2Cliente.Descomponer("Local", "LOCAL 6 · RÓTULO: EL EDÉN");
            Assert.AreEqual("6", local.NumeroLocal);
            Assert.AreEqual("EL EDÉN", local.Rotulo);
            CamposDireccion2 piso = Direccion2Cliente.Descomponer("Piso", "1º D");
            Assert.AreEqual("1º", piso.Piso);
            Assert.AreEqual("D", piso.Puerta);
            Assert.AreEqual("4", Direccion2Cliente.Descomponer("Nave", "NAVE 4").NumeroNave);
            Assert.IsNull(Direccion2Cliente.Descomponer("Local", null).TipoLugar);
        }

        // ---- Qué hace el PUT ----

        private static ClienteCrear Modificar(string direccion, string tipo = null, bool quitar = false) => new ClienteCrear
        {
            Empresa = "1", Cliente = "15191", Contacto = "0", Direccion = direccion, TipoLugar = tipo,
            Rotulo = tipo == TiposLugar.LOCAL ? "Marco Aldany" : null, QuitarDireccion2 = quitar
        };

        [TestMethod]
        public void ParaModificar_SinCampos_NoTocaLaGuardada_AunqueCambieLaDireccionSiNoSeExige()
        {
            // Compatibilidad: NestoApp y Nesto anteriores no mandan los campos.
            Assert.AreEqual(DecisionDireccion2Modificar.Acciones.NoTocar, Direccion2Cliente.ParaModificar(Modificar("CALLE MAYOR, 3"), "CALLE MAYOR, 3", exigir: true).Accion);
            Assert.AreEqual(DecisionDireccion2Modificar.Acciones.NoTocar, Direccion2Cliente.ParaModificar(Modificar("CALLE NUEVA, 1"), "CALLE MAYOR, 3", exigir: false).Accion);
        }

        [TestMethod]
        public void ParaModificar_CambiaLaDireccionYSeExige_SinTipoOQuitandola_400()
        {
            DecisionDireccion2Modificar sinTipo = Direccion2Cliente.ParaModificar(Modificar("CALLE NUEVA, 1"), "CALLE MAYOR, 3", exigir: true);
            DecisionDireccion2Modificar quitando = Direccion2Cliente.ParaModificar(Modificar("CALLE NUEVA, 1", quitar: true), "CALLE MAYOR, 3", exigir: true);

            Assert.AreEqual(Direccion2Cliente.MOTIVO_SIN_TIPO, sinTipo.Motivo);
            Assert.AreEqual(Direccion2Cliente.MOTIVO_SIN_TIPO, quitando.Motivo);
            Assert.IsTrue(Direccion2Cliente.ParaModificar(Modificar("calle mayor, 3 "), "CALLE MAYOR, 3", exigir: true).Valida, "mismas letras = no cambia");
        }

        [TestMethod]
        public void ParaModificar_ConTipo_ComponeConLaReglaDelAlta_OSeQuita()
        {
            DecisionDireccion2Modificar local = Direccion2Cliente.ParaModificar(Modificar("CALLE MAYOR, 3", TiposLugar.LOCAL), "CALLE MAYOR, 3", exigir: false);
            Assert.AreEqual(DecisionDireccion2Modificar.Acciones.Guardar, local.Accion);
            Assert.AreEqual("LOCAL · RÓTULO: MARCO ALDANY", local.Direccion2.Direccion2);
            Assert.AreEqual(TiposLugar.LOCAL, local.Direccion2.TipoLugar);

            ClienteCrear sinRotulo = Modificar("CALLE MAYOR, 3", TiposLugar.LOCAL);
            sinRotulo.Rotulo = null;
            Assert.AreEqual(Direccion2Cliente.MOTIVO_LOCAL, Direccion2Cliente.ParaModificar(sinRotulo, "CALLE MAYOR, 3", exigir: false).Motivo, "con tipo se valida siempre");

            Assert.AreEqual(DecisionDireccion2Modificar.Acciones.Quitar,
                Direccion2Cliente.ParaModificar(Modificar("CALLE MAYOR, 3", quitar: true), "CALLE MAYOR, 3", exigir: true).Accion);
        }

        // ---- GET GetClienteCrear ----

        [TestMethod]
        public async Task ConstruirClienteCrear_TraeLaDireccion2Descompuesta_YSinFilaNada()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            var gestor = new GestorClientes(servicio, A.Fake<IServicioAgencias>(), new SincronizacionEventWrapper(A.Fake<ISincronizacionEventPublisher>()));
            A.CallTo(() => servicio.BuscarCliente("1", "15191", "0")).Returns(new Cliente { Empresa = "1", Nº_Cliente = "15191", Contacto = "0", Dirección = "CALLE MAYOR, 3" });
            A.CallTo(() => servicio.LeerDireccion2Clientes("1", A<IEnumerable<string>>._)).Returns(new Dictionary<string, Direccion2Contacto>
            {
                ["15191|0"] = new Direccion2Contacto { Cliente = "15191", Contacto = "0", Direccion2 = "LOCAL · RÓTULO: MARCO ALDANY", TipoLugar = "Local" }
            });

            ClienteCrear modificar = await gestor.ConstruirClienteCrear("1", "15191", "0");

            Assert.AreEqual("CALLE MAYOR, 3", modificar.Direccion);
            Assert.AreEqual("Local", modificar.TipoLugar);
            Assert.AreEqual("MARCO ALDANY", modificar.Rotulo);
            Assert.IsNull(modificar.NumeroLocal);
            Assert.AreEqual("LOCAL · RÓTULO: MARCO ALDANY", modificar.Direccion2);

            A.CallTo(() => servicio.LeerDireccion2Clientes("1", A<IEnumerable<string>>._)).Returns(Task.FromResult<Dictionary<string, Direccion2Contacto>>(null));
            ClienteCrear sin = await gestor.ConstruirClienteCrear("1", "15191", "0");
            Assert.IsNull(sin.TipoLugar);
            Assert.IsNull(sin.Direccion2);
        }
    }
}
