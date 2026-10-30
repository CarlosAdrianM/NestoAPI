using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models.EscanerRemoto;
using System;
using System.Collections.Generic;
using System.Security.Claims;

namespace NestoAPI.Tests.Infraestructure.Notificaciones
{
    /// <summary>NestoAPI#768: escáner remoto (Ariadna lee, el Nesto del mismo usuario lo recibe).</summary>
    [TestClass]
    public class EscanerRemotoTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc);

        private ConexionesEscanerRemoto conexiones;
        private List<(string Conexion, LecturaEscanerRemotoAviso Aviso)> mandadas;
        private DateTime ahora;
        private ServicioEscanerRemoto servicio;

        [TestInitialize]
        public void Preparar()
        {
            conexiones = new ConexionesEscanerRemoto();
            mandadas = new List<(string, LecturaEscanerRemotoAviso)>();
            ahora = T0;
            servicio = new ServicioEscanerRemoto(conexiones, (c, a) => mandadas.Add((c, a)), () => ahora);
        }

        [DataTestMethod]
        [DataRow("NUEVAVISION\\Alfredo", "Alfredo")]
        [DataRow("Alfredo", "Alfredo")]
        [DataRow("  nuevavision\\alfredo  ", "alfredo")]
        [DataRow("", null)]
        [DataRow(null, null)]
        [DataRow("NUEVAVISION\\", null)]
        public void SinDominio_QuitaElDominioYRecorta(string usuario, string esperado)
        {
            Assert.AreEqual(esperado, UsuarioEscanerRemoto.SinDominio(usuario));
        }

        [TestMethod]
        public void Clave_CasaIdentityYWindowsSinDistinguirMayusculas()
        {
            Assert.AreEqual(UsuarioEscanerRemoto.Clave("Alfredo"), UsuarioEscanerRemoto.Clave("NUEVAVISION\\ALFREDO"));
            Assert.AreNotEqual(UsuarioEscanerRemoto.Clave("Alfredo"), UsuarioEscanerRemoto.Clave("NUEVAVISION\\Alfred"));
        }

        [TestMethod]
        public void Elegir_SinNestoAbierto_NoHayConexion()
        {
            conexiones.Actividad("c1", "NUEVAVISION\\Pedro", "RDS2016", true, T0);

            var elegido = conexiones.Elegir("Alfredo");

            Assert.IsNull(elegido.Conexion);
            Assert.AreEqual(0, elegido.Abiertos);
        }

        [TestMethod]
        public void Elegir_VariosNesto_ElDelFocoMasReciente()
        {
            conexiones.Actividad("c1", "NUEVAVISION\\Alfredo", "RDS2016", true, T0);
            conexiones.Actividad("c2", "NUEVAVISION\\Alfredo", "PC-ALFREDO", true, T0.AddMinutes(1));
            conexiones.Actividad("c3", "NUEVAVISION\\Pedro", "RDS2016", true, T0.AddMinutes(2));

            var elegido = conexiones.Elegir("alfredo");

            Assert.AreEqual("c2", elegido.Conexion);
            Assert.AreEqual("PC-ALFREDO", elegido.Equipo);
            Assert.AreEqual(2, elegido.Abiertos);

            conexiones.Actividad("c1", "NUEVAVISION\\Alfredo", null, true, T0.AddMinutes(3));
            Assert.AreEqual("c1", conexiones.Elegir("Alfredo").Conexion);
            Assert.AreEqual("RDS2016", conexiones.Elegir("Alfredo").Equipo, "Sin equipo en la actividad se queda el que dijo al presentarse");
        }

        [TestMethod]
        public void Elegir_PresentarseSinFoco_NoLeQuitaElSitioAlQueTuvoElFoco()
        {
            conexiones.Actividad("c1", "NUEVAVISION\\Alfredo", "RDS2016", true, T0);
            // c2 se conecta (o reconecta) con la ventana sin foco
            conexiones.Actividad("c2", "NUEVAVISION\\Alfredo", "RDS2019", false, T0.AddMinutes(5));
            // y c1 se vuelve a presentar sin foco: no pierde su último foco
            conexiones.Actividad("c1", "NUEVAVISION\\Alfredo", "RDS2016", false, T0.AddMinutes(6));

            Assert.AreEqual("c1", conexiones.Elegir("Alfredo").Conexion);
        }

        [TestMethod]
        public void Elegir_NingunoHaTenidoElFoco_ElPresentadoMasTarde()
        {
            conexiones.Actividad("c1", "NUEVAVISION\\Alfredo", "RDS2016", false, T0);
            conexiones.Actividad("c2", "NUEVAVISION\\Alfredo", "RDS2019", false, T0);

            Assert.AreEqual("c2", conexiones.Elegir("Alfredo").Conexion);
        }

        [TestMethod]
        public void Quitar_ElNestoQueSeCierraDejaDeContar()
        {
            conexiones.Actividad("c1", "NUEVAVISION\\Alfredo", "RDS2016", true, T0);
            conexiones.Actividad("c2", "NUEVAVISION\\Alfredo", "RDS2019", true, T0.AddMinutes(1));

            conexiones.Quitar("c2");

            Assert.AreEqual("c1", conexiones.Elegir("Alfredo").Conexion);
            conexiones.Quitar("c1");
            Assert.IsNull(conexiones.Elegir("Alfredo").Conexion);
        }

        [TestMethod]
        public void Destino_ConNesto_DiceAQuienVa()
        {
            conexiones.Actividad("c1", "NUEVAVISION\\Alfredo", "RDS2016", true, T0);

            DestinoEscanerRemotoDTO destino = servicio.Destino("Alfredo");

            Assert.IsTrue(destino.HayNesto);
            Assert.IsFalse(destino.Entregado);
            Assert.AreEqual("Alfredo", destino.Usuario);
            Assert.AreEqual("RDS2016", destino.Equipo);
            Assert.AreEqual(1, destino.NestosAbiertos);
            Assert.AreEqual("Enviando a Nesto de Alfredo (RDS2016).", destino.Mensaje);
            Assert.AreEqual(0, mandadas.Count, "Preguntar el destino no manda nada");
        }

        [TestMethod]
        public void Destino_SinNesto_LoDice()
        {
            DestinoEscanerRemotoDTO destino = servicio.Destino("Alfredo");

            Assert.IsFalse(destino.HayNesto);
            Assert.AreEqual("No hay ningún Nesto abierto con su usuario.", destino.Mensaje);
        }

        [TestMethod]
        public void Enviar_MandaSoloALaConexionDelMismoUsuario()
        {
            conexiones.Actividad("c-pedro", "NUEVAVISION\\Pedro", "RDS2016", true, T0.AddMinutes(5));
            conexiones.Actividad("c-alfredo", "NUEVAVISION\\Alfredo", "RDS2016", true, T0);
            var fecha = new DateTime(2026, 10, 30, 10, 15, 0);

            DestinoEscanerRemotoDTO respuesta = servicio.Enviar("Alfredo",
                new LecturaEscanerRemotoDTO { Codigo = "8412345678901", ClaveIdempotencia = "k1", FechaHora = fecha });

            Assert.AreEqual(1, mandadas.Count);
            Assert.AreEqual("c-alfredo", mandadas[0].Conexion);
            Assert.AreEqual("8412345678901", mandadas[0].Aviso.Codigo);
            Assert.AreEqual("k1", mandadas[0].Aviso.ClaveIdempotencia);
            Assert.AreEqual(fecha, mandadas[0].Aviso.FechaHora);
            Assert.AreEqual("Alfredo", mandadas[0].Aviso.Usuario);
            Assert.IsTrue(respuesta.Entregado);
            Assert.AreEqual("Enviado a Nesto de Alfredo (RDS2016).", respuesta.Mensaje);
        }

        [TestMethod]
        public void Enviar_SinNesto_NoMandaNadaYLoDice()
        {
            conexiones.Actividad("c-pedro", "NUEVAVISION\\Pedro", "RDS2016", true, T0);

            DestinoEscanerRemotoDTO respuesta = servicio.Enviar("Alfredo", new LecturaEscanerRemotoDTO { Codigo = "123", ClaveIdempotencia = "k1" });

            Assert.AreEqual(0, mandadas.Count);
            Assert.IsFalse(respuesta.Entregado);
            Assert.IsFalse(respuesta.HayNesto);
            Assert.AreEqual("No hay ningún Nesto abierto con su usuario: no se ha enviado.", respuesta.Mensaje);
        }

        [TestMethod]
        public void Enviar_MismaClave_NoSeMandaDosVecesYDaLaMismaRespuesta()
        {
            conexiones.Actividad("c1", "NUEVAVISION\\Alfredo", "RDS2016", true, T0);
            var lectura = new LecturaEscanerRemotoDTO { Codigo = "123", ClaveIdempotencia = "k1" };

            DestinoEscanerRemotoDTO primera = servicio.Enviar("Alfredo", lectura);
            DestinoEscanerRemotoDTO segunda = servicio.Enviar("ALFREDO", lectura);

            Assert.AreEqual(1, mandadas.Count);
            Assert.AreSame(primera, segunda);
        }

        [TestMethod]
        public void Enviar_MismaClaveDeOtroUsuario_NoEsLaMisma()
        {
            conexiones.Actividad("c1", "NUEVAVISION\\Alfredo", "RDS2016", true, T0);
            conexiones.Actividad("c2", "NUEVAVISION\\Pedro", "RDS2016", true, T0);

            _ = servicio.Enviar("Alfredo", new LecturaEscanerRemotoDTO { Codigo = "123", ClaveIdempotencia = "k1" });
            _ = servicio.Enviar("Pedro", new LecturaEscanerRemotoDTO { Codigo = "456", ClaveIdempotencia = "k1" });

            Assert.AreEqual(2, mandadas.Count);
            Assert.AreEqual("c2", mandadas[1].Conexion);
        }

        [TestMethod]
        public void Enviar_SinNestoYLuegoConNesto_LaMismaClaveSiVa()
        {
            var lectura = new LecturaEscanerRemotoDTO { Codigo = "123", ClaveIdempotencia = "k1" };
            Assert.IsFalse(servicio.Enviar("Alfredo", lectura).Entregado);

            conexiones.Actividad("c1", "NUEVAVISION\\Alfredo", "RDS2016", true, T0);

            Assert.IsTrue(servicio.Enviar("Alfredo", lectura).Entregado);
            Assert.AreEqual(1, mandadas.Count);
        }

        [TestMethod]
        public void Enviar_LaClaveSeOlvidaPasadoElTiempo()
        {
            conexiones.Actividad("c1", "NUEVAVISION\\Alfredo", "RDS2016", true, T0);
            var lectura = new LecturaEscanerRemotoDTO { Codigo = "123", ClaveIdempotencia = "k1" };
            _ = servicio.Enviar("Alfredo", lectura);

            ahora = T0 + ServicioEscanerRemoto.DURACION_IDEMPOTENCIA + TimeSpan.FromSeconds(1);
            _ = servicio.Enviar("Alfredo", lectura);

            Assert.AreEqual(2, mandadas.Count);
        }

        [TestMethod]
        public void Enviar_SinClave_SeMandaCadaVez()
        {
            conexiones.Actividad("c1", "NUEVAVISION\\Alfredo", "RDS2016", true, T0);

            _ = servicio.Enviar("Alfredo", new LecturaEscanerRemotoDTO { Codigo = "123" });
            _ = servicio.Enviar("Alfredo", new LecturaEscanerRemotoDTO { Codigo = "123" });

            Assert.AreEqual(2, mandadas.Count);
        }

        [TestMethod]
        public void Enviar_SiFallaElEnvio_NoSeDaPorEntregadoNiSeRecuerdaLaClave()
        {
            int intentos = 0;
            var falla = new ServicioEscanerRemoto(conexiones, (c, a) =>
            {
                intentos++;
                if (intentos == 1)
                {
                    throw new InvalidOperationException("SignalR caído");
                }
            }, () => ahora);
            conexiones.Actividad("c1", "NUEVAVISION\\Alfredo", "RDS2016", true, T0);
            var lectura = new LecturaEscanerRemotoDTO { Codigo = "123", ClaveIdempotencia = "k1" };

            DestinoEscanerRemotoDTO primera = falla.Enviar("Alfredo", lectura);
            DestinoEscanerRemotoDTO segunda = falla.Enviar("Alfredo", lectura);

            Assert.IsFalse(primera.Entregado);
            Assert.AreEqual(ServicioEscanerRemoto.MENSAJE_FALLO_ENVIO, primera.Mensaje);
            Assert.IsTrue(segunda.Entregado);
            Assert.AreEqual(2, intentos);
        }

        [TestMethod]
        public void NombreNesto_SinEquipo_SoloElUsuario()
        {
            Assert.AreEqual("Nesto de Alfredo", ServicioEscanerRemoto.NombreNesto("Alfredo", null));
            Assert.AreEqual("Nesto de Alfredo (RDS2016)", ServicioEscanerRemoto.NombreNesto("Alfredo", " RDS2016 "));
        }

        [TestMethod]
        public void EsCliente_ElTokenDeTiendasNuevaVision()
        {
            var cliente = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "a@b.es"), new Claim("cliente", "15191") }, "Bearer"));
            var empleado = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "NUEVAVISION\\Alfredo"), new Claim("IsEmployee", "true") }, "Bearer"));

            Assert.IsTrue(EscanerRemotoPermisos.EsCliente(cliente));
            Assert.IsFalse(EscanerRemotoPermisos.EsCliente(empleado));
            Assert.IsFalse(EscanerRemotoPermisos.EsCliente(null));
        }
    }
}
