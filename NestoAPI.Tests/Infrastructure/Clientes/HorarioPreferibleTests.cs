using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Models.Clientes;
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
    /// NestoAPI#763 (Nesto#584): «horario preferible» por cliente y contacto (se intenta respetar, sin compromiso). El formato en
    /// texto, las reglas de lo que se puede guardar, las propuestas sacadas de los comentarios reales de las fichas, el servicio y
    /// los endpoints.
    /// </summary>
    [TestClass]
    public class HorarioPreferibleTests
    {
        private static FranjaHorario F(int dia, string desde, string hasta)
            => new FranjaHorario(dia, FormatoHorarioPreferible.LeerHora(desde).Value, FormatoHorarioPreferible.LeerHora(hasta).Value);

        private static List<FranjaHorario> LV(params string[] horas)
        {
            var lista = new List<FranjaHorario>();
            for (int d = 1; d <= 5; d++)
            {
                for (int i = 0; i < horas.Length; i += 2)
                {
                    lista.Add(F(d, horas[i], horas[i + 1]));
                }
            }
            return lista;
        }

        // ================================================================ formato

        [TestMethod]
        public void Texto_AgrupaLosDiasConLasMismasHoras_EnIntervalos()
        {
            var franjas = LV("17:00", "20:00", "10:00", "14:00");
            franjas.Add(F(6, "10:00", "13:00"));

            Assert.AreEqual("L-V 10:00-14:00, 17:00-20:00; S 10:00-13:00", FormatoHorarioPreferible.Texto(franjas));
        }

        [TestMethod]
        public void Texto_DiasSueltosYDosSeguidos_ConComas_YSinFranjasVacio()
        {
            var franjas = new List<FranjaHorario> { F(1, "9:00", "13:00"), F(3, "9:00", "13:00"), F(4, "9:00", "13:00"), F(5, "9:00", "13:00"), F(7, "11:00", "14:00") };

            Assert.AreEqual("L, X-V 09:00-13:00; D 11:00-14:00", FormatoHorarioPreferible.Texto(franjas));
            Assert.AreEqual("L, M 09:00-13:00", FormatoHorarioPreferible.Texto(new[] { F(1, "9:00", "13:00"), F(2, "9:00", "13:00") }));
            Assert.AreEqual(string.Empty, FormatoHorarioPreferible.Texto(null));
            Assert.AreEqual("09:00-13:00", FormatoHorarioPreferible.TextoDia(franjas, 3));
            Assert.AreEqual(string.Empty, FormatoHorarioPreferible.TextoDia(franjas, 6));
        }

        [TestMethod]
        public void Leer_ElTextoDeVuelta_DaLasMismasFranjas()
        {
            const string texto = "L-V 10:00-14:00, 17:00-20:00; S 10:00-13:00";

            List<FranjaHorario> franjas = FormatoHorarioPreferible.Leer(texto, out string error);

            Assert.IsNull(error);
            Assert.AreEqual(11, franjas.Count);
            Assert.AreEqual(texto, FormatoHorarioPreferible.Texto(franjas));
            Assert.AreEqual("L, X 09:00-13:00", FormatoHorarioPreferible.Texto(FormatoHorarioPreferible.Leer("l,x 9-13", out _)));
            Assert.AreEqual(0, FormatoHorarioPreferible.Leer("  ", out error).Count, "En blanco: sin horario");
            Assert.IsNull(error);
        }

        [TestMethod]
        public void Leer_TextoMalo_NullYElMotivoDeUsted()
        {
            Assert.IsNull(FormatoHorarioPreferible.Leer("por las mañanas", out string error));
            StringAssert.Contains(error, "Escriba los días");
            Assert.IsNull(FormatoHorarioPreferible.Leer("V-L 10:00-14:00", out error));
            StringAssert.Contains(error, "días");
            Assert.IsNull(FormatoHorarioPreferible.Leer("L 25:00-26:00", out error));
            StringAssert.Contains(error, "10:00-14:00");
        }

        [TestMethod]
        public void Validar_AlReves_Solapadas_YMasDeDosPorDia()
        {
            StringAssert.Contains(FormatoHorarioPreferible.Validar(new[] { F(1, "14:00", "10:00") }), "anterior");
            StringAssert.Contains(FormatoHorarioPreferible.Validar(new[] { F(2, "10:00", "14:00"), F(2, "13:00", "18:00") }), "se solapan");
            StringAssert.Contains(FormatoHorarioPreferible.Validar(new[] { F(3, "8:00", "9:00"), F(3, "10:00", "11:00"), F(3, "12:00", "13:00") }), "como mucho");
            Assert.IsNull(FormatoHorarioPreferible.Validar(LV("10:00", "14:00", "17:00", "20:00")));
            Assert.IsNull(FormatoHorarioPreferible.Leer(new[] { new FranjaHorarioPreferibleDTO { DiaSemana = 8, Desde = "10:00", Hasta = "11:00" } }, out string error));
            StringAssert.Contains(error, "de 1 (lunes) a 7 (domingo)");
        }

        [TestMethod]
        public void DiaSemana_LunesEsUno_DomingoSiete()
        {
            Assert.AreEqual(1, FormatoHorarioPreferible.DiaSemana(DayOfWeek.Monday));
            Assert.AreEqual(5, FormatoHorarioPreferible.DiaSemana(DayOfWeek.Friday));
            Assert.AreEqual(7, FormatoHorarioPreferible.DiaSemana(DayOfWeek.Sunday));
        }

        // ================================================================ propuestas desde los comentarios
        // Las formas de escribir el horario son las de las fichas reales (30/10/26); los nombres, teléfonos, direcciones y
        // números de cliente están cambiados.

        [DataTestMethod]
        [DataRow("Móvil solo para urgencias  CIERRA LOS LUNES .     Horario: 9 a 13:30 / 16:00 a 22:00   || CENTRO DE ESTETICA EJEMPLO. LUNES CERRADO", "M-V 09:00-13:30, 16:00-22:00")]
        [DataRow("Vendedor (14/07/04)  Persona,  14/ abril. Preguntar siempre como va facturado || Horario: 09 á 18 Detrás de la farmacia", "L-V 09:00-18:00")]
        [DataRow("HORARIO:  CIERRA DE 14:30 A 16:00   || 10 a 13:30 y de 17 a 20:30h", "L-V 10:00-13:30, 17:00-20:30")]
        [DataRow("HORARIO 10:00 A 13:30 Y16:00 A 19:00H.MARTES CERRADO. || De 10:00 a 13:30 y 16:00 a 19:00H.Martes cerrado.", "L, X-V 10:00-13:30, 16:00-19:00")]
        [DataRow("SOLO ESTETICA  ahora es para uso personal || Horario: De 8´00h a 16´00h.", "L-V 08:00-16:00")]
        [DataRow("horario de 11 a 2 y de 5 a 8 h   || ", "L-V 11:00-14:00, 17:00-20:00")]
        [DataRow("CLIENTE ONLINE || HORARIO DE ENTREGA 16:30 20:30", "L-V 16:30-20:30")]
        [DataRow("NOTA. 7/2/03. ES UNA CLINICA. 12/07/04-Comercial- A partir de agosto cambian || HORARIO DE 10'00-13'00 Y DE 16'00-20'00", "L-V 10:00-13:00, 16:00-20:00")]
        [DataRow("PROPUESTA  SE COBRA EN C/ MAYOR,6  MADRID || HORARIO DE 9 A 2 Y DE 4:30 A 8", "L-V 09:00-14:00, 16:30-20:00")]
        [DataRow("NOTA: HORARIO DE 9H A 13,30H. Y DE 17,30H A 20,30H. || ", "L-V 09:00-13:30, 17:30-20:30")]
        [DataRow("Horario de 16 a 21h  ATT DE LA DUEÑA   || CERRADO DE 13 á 16", "L-V 16:00-21:00")]
        [DataRow("ES UNA PELUQUERIA  HORARIO:10-13.30/15-20H    30/01/2003 NOTA. LA CCC ESTA MAL || ", "L-V 10:00-13:30, 15:00-20:00")]
        [DataRow("Nota 14-06-04: Es una empresa de informatica han comprado una camilla || Horario: De 10h a 18h.", "L-V 10:00-18:00")]
        [DataRow("VENDEDOR (16/01/03) TIENE UN 40% DESCUENTO EN UNA MARCA || HORARIO 9:30 A 13:30 - 17:00 A 20:30", "L-V 09:30-13:30, 17:00-20:30")]
        [DataRow("C/1111 (nota)  MANDAR COPIA DE CORREO || HORARIO:\r\n10 a 13:30 / 17 a 21", "L-V 10:00-13:30, 17:00-21:00")]
        [DataRow("NOTA(16/02/06) AHORA ES C/2222 || Horario 10h-14h / 15h-18h", "L-V 10:00-14:00, 15:00-18:00")]
        [DataRow("los miercoles no esta en tienda ella || horario: 10:00 - 13:00\r\n             17:00 - 21:00", "L-V 10:00-13:00, 17:00-21:00")]
        [DataRow("TIENE UN 15% DE DESCUENTO || horario 9.30 a 17h.", "L-V 09:30-17:00")]
        [DataRow("Horario de 9:00a 13:45 y  de 15:00 a 17:45 proveedor  999   || ", "L-V 09:00-13:45, 15:00-17:45")]
        [DataRow("Descuentos especiales dependiendo del importe:hasta 90€ un 10%, hasta 200€ un 20%  05/08/11 - MARCA 10%   || 9-2 y 5-8:30 respetar horario o no dejar.", "L-V 09:00-14:00, 17:00-20:30")]
        [DataRow("tiene otra ficha la 3333.   || HORARIO MARTES A VIERNES 10 A 13 Y 16 A 19 HORAS", "M-V 10:00-13:00, 16:00-19:00")]
        [DataRow("LLAMAD CON IMPORTE. XX, 28/12/15.   || HORARIO. LUNES A JUEVES DE 11:00 A 19:00", "L-J 11:00-19:00")]
        [DataRow("LUNES CERRADO  HORARIO 9:30-13:30 17:00-20:00 || ", "M-V 09:30-13:30, 17:00-20:00")]
        [DataRow("CLIENTE DE UNA CADENA || Horario de entrega de 9 a 14 h. de lunes a viernes", "L-V 09:00-14:00")]
        [DataRow("DISTRIBUIDOR  PERSONA 600000001  OTRA 600000002   || Horario de 8h a 14h.", "L-V 08:00-14:00")]
        public void Proponer_ComentariosReales(string comentarios, string esperado)
        {
            Assert.AreEqual(esperado, ExtractorHorarioComentarios.ProponerTexto(comentarios));
        }

        [DataTestMethod]
        [DataRow("trabaja con otra marca. esteticista. || Horario hasta las 14.00h")]
        [DataRow("Horario:  L- 10 a 19h.  M - 10 A 13h.  X, J, V - 10 A 15 Y 17 A 19h. || No dejar pedidos en otro establecimiento.")]
        [DataRow("Ya no tiene relación con el 4444 || NO DEJAR SI NO PAGA / HORARIO COMERCIAL")]
        [DataRow("USO PERSONAL  Suprimo el nº de fax || Horario:a partir de las16h.")]
        [DataRow("ENTREGA EN HORARIO DE TARDE A PARTIR DE LAS 17 HORAS TELEFONO 600000003... || A PARTIR DE LAS 17 HORAS")]
        [DataRow("HORARIO 8 a 10, 11 a 13 y 16 a 20")]
        [DataRow("")]
        [DataRow(null)]
        public void Proponer_AnteLaDuda_NoPropone(string comentarios)
        {
            Assert.IsNull(ExtractorHorarioComentarios.Proponer(comentarios));
        }

        // ================================================================ servicio

        internal class RepositorioFalso : IRepositorioHorarioPreferible
        {
            public bool Script = true;
            public readonly List<FilaHorarioPreferible> Filas = new List<FilaHorarioPreferible>();
            public readonly List<FilaPropuestaHorario> Propuestas = new List<FilaPropuestaHorario>();
            public readonly HashSet<string> Contactos = new HashSet<string> { "1|15191|0" };
            public string UltimoTextoAceptado;

            public bool ScriptAplicado() => Script;

            public Task<bool> ExisteContacto(string empresa, string cliente, string contacto)
                => Task.FromResult(Contactos.Contains(RepositorioHorarioPreferible.Clave(empresa, cliente, contacto)));

            public Task<List<FilaHorarioPreferible>> Leer(string empresa, string cliente, string contacto)
                => Task.FromResult(Filas.Where(f => RepositorioHorarioPreferible.Clave(f.Empresa, f.Cliente, f.Contacto) == RepositorioHorarioPreferible.Clave(empresa, cliente, contacto)).ToList());

            public Task<List<FilaHorarioPreferible>> LeerVarios(IReadOnlyCollection<(string Empresa, string Cliente, string Contacto)> contactos)
            {
                var claves = new HashSet<string>(contactos.Select(c => RepositorioHorarioPreferible.Clave(c.Empresa, c.Cliente, c.Contacto)));
                return Task.FromResult(Filas.Where(f => claves.Contains(RepositorioHorarioPreferible.Clave(f.Empresa, f.Cliente, f.Contacto))).ToList());
            }

            public Task Guardar(string empresa, string cliente, string contacto, IReadOnlyCollection<FranjaHorario> franjas, string usuario, string textoAceptado)
            {
                string clave = RepositorioHorarioPreferible.Clave(empresa, cliente, contacto);
                _ = Filas.RemoveAll(f => RepositorioHorarioPreferible.Clave(f.Empresa, f.Cliente, f.Contacto) == clave);
                Filas.AddRange(franjas.Select(f => new FilaHorarioPreferible
                {
                    Empresa = empresa, Cliente = cliente, Contacto = contacto, DiaSemana = (byte)f.DiaSemana, Desde = f.Desde, Hasta = f.Hasta,
                    Usuario = usuario, FechaModificacion = new DateTime(2026, 10, 30, 12, 0, 0)
                }));
                foreach (FilaPropuestaHorario p in Propuestas.Where(p => RepositorioHorarioPreferible.Clave(p.Empresa, p.Cliente, p.Contacto) == clave
                    && p.Estado == PropuestaHorarioPreferibleDTO.ESTADO_POR_REVISAR))
                {
                    p.Estado = PropuestaHorarioPreferibleDTO.ESTADO_ACEPTADA;
                }
                UltimoTextoAceptado = textoAceptado;
                return Task.CompletedTask;
            }

            public Task<List<FilaPropuestaHorario>> LeerPropuestas(string estado, int? id = null, string empresa = null, string cliente = null, string contacto = null)
                => Task.FromResult(Propuestas.Where(p => p.Estado == estado && (!id.HasValue || p.Id == id.Value)
                    && (cliente == null || RepositorioHorarioPreferible.Clave(p.Empresa, p.Cliente, p.Contacto) == RepositorioHorarioPreferible.Clave(empresa, cliente, contacto)))
                    .ToList());

            public Task<bool> ResolverPropuesta(int id, string estado, string usuario, string textoAceptado)
            {
                FilaPropuestaHorario p = Propuestas.FirstOrDefault(x => x.Id == id && x.Estado == PropuestaHorarioPreferibleDTO.ESTADO_POR_REVISAR);
                if (p == null)
                {
                    return Task.FromResult(false);
                }
                p.Estado = estado;
                return Task.FromResult(true);
            }
        }

        private RepositorioFalso repositorio;
        private ServicioHorarioPreferible servicio;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = new RepositorioFalso();
            servicio = new ServicioHorarioPreferible(repositorio);
        }

        private FilaPropuestaHorario Propuesta(int id, string texto, string cliente = "15191")
        {
            var p = new FilaPropuestaHorario
            {
                Id = id, Empresa = "1", Cliente = cliente, Contacto = "0", Nombre = "PELUQUERÍA LA BONITA", TextoOrigen = texto,
                Estado = PropuestaHorarioPreferibleDTO.ESTADO_POR_REVISAR, Fecha = new DateTime(2026, 10, 30)
            };
            repositorio.Propuestas.Add(p);
            return p;
        }

        [TestMethod]
        public async Task Leer_SinHorario_FranjasVacias_ConElAviso_YSuPropuestaPendiente()
        {
            _ = Propuesta(7, "HORARIO DE 10 A 14");

            ResultadoHorarioPreferible<HorarioPreferibleDTO> r = await servicio.Leer(null, " 15191 ", null);

            Assert.IsTrue(r.Ok);
            Assert.AreEqual("1", r.Contenido.Empresa);
            Assert.AreEqual("0", r.Contenido.Contacto, "Sin contacto: el 0");
            Assert.AreEqual(0, r.Contenido.Franjas.Count);
            Assert.AreEqual(string.Empty, r.Contenido.Texto);
            StringAssert.Contains(r.Contenido.Aviso, "no hay compromiso de respetarlo");
            Assert.AreEqual(7, r.Contenido.PropuestaPendiente.Id);
            Assert.AreEqual("L-V 10:00-14:00", r.Contenido.PropuestaPendiente.HorarioPropuesto);
        }

        [TestMethod]
        public async Task Leer_SinScript_SinHorario_YNoFalla()
        {
            repositorio.Script = false;

            ResultadoHorarioPreferible<HorarioPreferibleDTO> r = await servicio.Leer("1", "15191", "0");

            Assert.IsTrue(r.Ok);
            Assert.AreEqual(0, r.Contenido.Franjas.Count);
            Assert.IsNull(r.Contenido.PropuestaPendiente);
        }

        [TestMethod]
        public async Task Guardar_ConFranjas_LasDevuelveOrdenadas_ConTexto_YResuelveLaPropuesta()
        {
            _ = Propuesta(7, "HORARIO DE 10 A 14");
            var peticion = new HorarioPreferibleDTO
            {
                Franjas = new List<FranjaHorarioPreferibleDTO>
                {
                    new FranjaHorarioPreferibleDTO { DiaSemana = 6, Desde = "10:00", Hasta = "13:00" },
                    new FranjaHorarioPreferibleDTO { DiaSemana = 1, Desde = "9:30", Hasta = "14:00" }
                }
            };

            ResultadoHorarioPreferible<HorarioPreferibleDTO> r = await servicio.Guardar("1", "15191", "0", peticion, "Carlos");

            Assert.IsTrue(r.Ok, r.Mensaje);
            Assert.AreEqual("L 09:30-14:00; S 10:00-13:00", r.Contenido.Texto);
            Assert.AreEqual(1, r.Contenido.Franjas[0].DiaSemana);
            Assert.AreEqual("09:30", r.Contenido.Franjas[0].Desde);
            Assert.AreEqual("Carlos", r.Contenido.Usuario);
            Assert.IsNull(r.Contenido.PropuestaPendiente, "Al guardar a mano la propuesta queda resuelta");
            Assert.AreEqual("L 09:30-14:00; S 10:00-13:00", repositorio.UltimoTextoAceptado);
        }

        [TestMethod]
        public async Task Guardar_ConTexto_YSinFranjasBorra()
        {
            ResultadoHorarioPreferible<HorarioPreferibleDTO> r = await servicio.Guardar("1", "15191", "0", new HorarioPreferibleDTO { Texto = "L-V 10:00-14:00" }, "Carlos");
            Assert.AreEqual(5, r.Contenido.Franjas.Count);

            r = await servicio.Guardar("1", "15191", "0", new HorarioPreferibleDTO(), "Carlos");

            Assert.IsTrue(r.Ok);
            Assert.AreEqual(0, repositorio.Filas.Count);
            Assert.AreEqual(string.Empty, r.Contenido.Texto);
        }

        [TestMethod]
        public async Task Guardar_Invalido400_ContactoQueNoExiste404_SinScript400()
        {
            var solapadas = new HorarioPreferibleDTO { Texto = "L 10:00-14:00, 13:00-18:00" };
            ResultadoHorarioPreferible<HorarioPreferibleDTO> r = await servicio.Guardar("1", "15191", "0", solapadas, "Carlos");
            Assert.AreEqual(HttpStatusCode.BadRequest, r.Error);
            StringAssert.Contains(r.Mensaje, "se solapan");

            r = await servicio.Guardar("1", "99999", "0", new HorarioPreferibleDTO { Texto = "L 10:00-14:00" }, "Carlos");
            Assert.AreEqual(HttpStatusCode.NotFound, r.Error);

            r = await servicio.Guardar("1", "15191", "0", null, "Carlos");
            Assert.AreEqual(HttpStatusCode.BadRequest, r.Error);

            repositorio.Script = false;
            r = await servicio.Guardar("1", "15191", "0", new HorarioPreferibleDTO { Texto = "L 10:00-14:00" }, "Carlos");
            Assert.AreEqual(HttpStatusCode.BadRequest, r.Error);
            Assert.AreEqual(ServicioHorarioPreferible.MENSAJE_FALTA_SCRIPT, r.Mensaje);
            Assert.AreEqual(0, repositorio.Filas.Count);
        }

        [TestMethod]
        public async Task Propuestas_ConLoQueProponenLasReglas_YNullSiNoSaben()
        {
            _ = Propuesta(1, "HORARIO DE 9 A 2 Y DE 4:30 A 8");
            _ = Propuesta(2, "HORARIO COMERCIAL", "20461");
            repositorio.Propuestas.Add(new FilaPropuestaHorario { Id = 3, Empresa = "1", Cliente = "1", Contacto = "0", TextoOrigen = "HORARIO 9 A 14", Estado = PropuestaHorarioPreferibleDTO.ESTADO_DESCARTADA });

            List<PropuestaHorarioPreferibleDTO> lista = await servicio.Propuestas();

            CollectionAssert.AreEqual(new[] { 1, 2 }, lista.Select(p => p.Id).ToList(), "Solo las que están por revisar");
            Assert.AreEqual("L-V 09:00-14:00, 16:30-20:00", lista[0].HorarioPropuesto);
            Assert.AreEqual(10, lista[0].Franjas.Count);
            Assert.IsNull(lista[1].HorarioPropuesto);
            Assert.AreEqual(0, lista[1].Franjas.Count);
            Assert.AreEqual("HORARIO COMERCIAL", lista[1].TextoOrigen);
        }

        [TestMethod]
        public async Task Aceptar_SinMandarNada_GuardaLoPropuesto_YCorregidoGuardaLoDeLaPersona()
        {
            _ = Propuesta(1, "HORARIO DE 10 A 14");

            ResultadoHorarioPreferible<HorarioPreferibleDTO> r = await servicio.Aceptar(1, null, "Laura");

            Assert.IsTrue(r.Ok, r.Mensaje);
            Assert.AreEqual("L-V 10:00-14:00", r.Contenido.Texto);
            Assert.AreEqual(PropuestaHorarioPreferibleDTO.ESTADO_ACEPTADA, repositorio.Propuestas[0].Estado);

            _ = Propuesta(2, "HORARIO DE 10 A 14");
            r = await servicio.Aceptar(2, new HorarioPreferibleDTO { Texto = "M-V 10:00-14:00" }, "Laura");
            Assert.AreEqual("M-V 10:00-14:00", r.Contenido.Texto);
        }

        [TestMethod]
        public async Task Aceptar_SinPropuestaDeLasReglasNiTexto400_YaRevisada404()
        {
            _ = Propuesta(1, "HORARIO COMERCIAL");

            ResultadoHorarioPreferible<HorarioPreferibleDTO> r = await servicio.Aceptar(1, new HorarioPreferibleDTO(), "Laura");
            Assert.AreEqual(HttpStatusCode.BadRequest, r.Error);
            StringAssert.Contains(r.Mensaje, "escríbalo usted o descarte");

            Assert.IsTrue((await servicio.Descartar(1, "Laura")).Ok);
            Assert.AreEqual(PropuestaHorarioPreferibleDTO.ESTADO_DESCARTADA, repositorio.Propuestas[0].Estado);
            Assert.AreEqual(HttpStatusCode.NotFound, (await servicio.Aceptar(1, null, "Laura")).Error);
            Assert.AreEqual(HttpStatusCode.NotFound, (await servicio.Descartar(1, "Laura")).Error);
        }

        [TestMethod]
        public async Task HorariosDe_VariosContactos_PorClave_YSinScriptVacio()
        {
            await servicio.Guardar("1", "15191", "0", new HorarioPreferibleDTO { Texto = "L-V 10:00-14:00" }, "Carlos");
            repositorio.Contactos.Add("1|20000|1");
            await servicio.Guardar("1", "20000", "1", new HorarioPreferibleDTO { Texto = "S 10:00-13:00" }, "Carlos");

            Dictionary<string, List<FranjaHorario>> h = await servicio.HorariosDe(new[] { ("1  ", "15191     ", "0  "), ("1", "20000", "0") });

            Assert.AreEqual(1, h.Count, "El 20000/0 no tiene (lo tiene el contacto 1)");
            Assert.AreEqual(5, h["1|15191|0"].Count);
            Dictionary<string, List<FranjaHorario>> deLaTres = await servicio.HorariosDe(new[] { ("3", "15191", "0") });
            Assert.AreEqual(5, deLaTres["3|15191|0"].Count, "Las otras empresas usan el horario de la 1");
            Assert.AreEqual("1", (await servicio.Leer("3", "15191", "0")).Contenido.Empresa);
            repositorio.Script = false;
            Assert.AreEqual(0, (await servicio.HorariosDe(new[] { ("1", "15191", "0") })).Count);
        }

        // ================================================================ controller

        /// <param name="grupos">Sin grupos: un empleado cualquiera (un vendedor). Las propuestas piden Administración, Almacén, Dirección o Informática.</param>
        private HorarioPreferibleController Controller(params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, @"NUEVAVISION\Laura") };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, @"NUEVAVISION\" + g)));
            var controller = new HorarioPreferibleController(servicio)
            {
                Request = new System.Net.Http.HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"))
            };
            return controller;
        }

        [TestMethod]
        public async Task Controller_EmpleadoSinGrupoDeRevision_403EnPropuestas_PeroGetYPutDelContactoOk()
        {
            // Revisión 30/10/26: «Horarios por revisar» solo para Administración, Almacén, Dirección e Informática; el horario de
            // un contacto lo siguen viendo y cambiando todos los empleados (los vendedores, en la ficha).
            HorarioPreferibleController controller = Controller("Vendedores");
            FilaPropuestaHorario propuesta = Propuesta(5, "HORARIO DE 10 A 14");

            IHttpActionResult propuestas = await controller.GetPropuestas();
            IHttpActionResult aceptar = await controller.PostAceptarPropuesta(5, null);
            IHttpActionResult descartar = await controller.PostDescartarPropuesta(5);
            Assert.AreEqual(PropuestaHorarioPreferibleDTO.ESTADO_POR_REVISAR, propuesta.Estado, "Ni aceptada ni descartada");
            Assert.AreEqual(0, repositorio.Filas.Count, "El aceptar no ha guardado nada");
            var put = await controller.PutHorarioPreferible("1", "15191", "0", new HorarioPreferibleDTO { Texto = "L-V 10:00-14:00" }) as OkNegotiatedContentResult<HorarioPreferibleDTO>;
            var get = await controller.GetHorarioPreferible("1", "15191", "0") as OkNegotiatedContentResult<HorarioPreferibleDTO>;

            foreach (IHttpActionResult r in new[] { propuestas, aceptar, descartar })
            {
                System.Net.Http.HttpResponseMessage respuesta = await r.ExecuteAsync(default);
                Assert.AreEqual(HttpStatusCode.Forbidden, respuesta.StatusCode);
                StringAssert.Contains(await respuesta.Content.ReadAsStringAsync(), "Administración, Almacén, Dirección e Informática");
            }
            Assert.IsNotNull(put);
            Assert.AreEqual("L-V 10:00-14:00", get.Content.Texto);
        }

        [DataTestMethod]
        [DataRow("Administración")]
        [DataRow("Almacén")]
        [DataRow("Dirección")]
        [DataRow("Informática")]
        public async Task Controller_GruposDeRevision_VenLasPropuestas(string grupo)
        {
            _ = Propuesta(5, "HORARIO DE 10 A 14");

            var propuestas = await Controller(grupo).GetPropuestas() as OkNegotiatedContentResult<List<PropuestaHorarioPreferibleDTO>>;

            Assert.IsNotNull(propuestas, grupo);
            Assert.AreEqual(1, propuestas.Content.Count);
        }

        [TestMethod]
        public async Task Controller_GetYPut_Ok_YElUsuarioSinDominio()
        {
            HorarioPreferibleController controller = Controller();

            var put = await controller.PutHorarioPreferible("1", "15191", "0", new HorarioPreferibleDTO { Texto = "L-V 10:00-14:00" }) as OkNegotiatedContentResult<HorarioPreferibleDTO>;
            var get = await controller.GetHorarioPreferible("1", "15191", "0") as OkNegotiatedContentResult<HorarioPreferibleDTO>;

            Assert.IsNotNull(put);
            Assert.AreEqual("L-V 10:00-14:00", get.Content.Texto);
            Assert.IsFalse(string.IsNullOrWhiteSpace(repositorio.Filas[0].Usuario));
        }

        [TestMethod]
        public async Task Controller_ClienteDeLaTienda_403EnTodo()
        {
            // Revisión 30/10/26: el token de TiendasNuevaVision (claim «cliente») solo lleva [Authorize]: ni ve ni cambia horarios
            // de otras fichas ni se lleva los comentarios internos de las propuestas.
            HorarioPreferibleController controller = Controller();
            controller.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "cliente@correo.es"), new Claim("cliente", "15191") }, "prueba"));
            _ = Propuesta(5, "HORARIO DE 10 A 14");

            IHttpActionResult get = await controller.GetHorarioPreferible("1", "15191", "0");
            IHttpActionResult put = await controller.PutHorarioPreferible("1", "15191", "0", new HorarioPreferibleDTO { Texto = "L-V 10:00-14:00" });
            IHttpActionResult propuestas = await controller.GetPropuestas();
            IHttpActionResult aceptar = await controller.PostAceptarPropuesta(5, null);
            IHttpActionResult descartar = await controller.PostDescartarPropuesta(5);

            foreach (IHttpActionResult r in new[] { get, put, propuestas, aceptar, descartar })
            {
                Assert.AreEqual(HttpStatusCode.Forbidden, (await r.ExecuteAsync(default)).StatusCode);
            }
            Assert.AreEqual(0, repositorio.Filas.Count, "No ha guardado nada");
        }

        [TestMethod]
        public async Task Controller_PutMalo400_PropuestasLista_Descartar204Y404()
        {
            HorarioPreferibleController controller = Controller(NestoAPI.Models.Constantes.GruposSeguridad.ADMINISTRACION);
            _ = Propuesta(5, "HORARIO DE 10 A 14");

            IHttpActionResult malo = await controller.PutHorarioPreferible("1", "15191", "0", new HorarioPreferibleDTO { Texto = "L 14:00-10:00" });
            var propuestas = await controller.GetPropuestas() as OkNegotiatedContentResult<List<PropuestaHorarioPreferibleDTO>>;
            var descartada = await controller.PostDescartarPropuesta(5) as StatusCodeResult;
            IHttpActionResult otraVez = await controller.PostDescartarPropuesta(5);

            Assert.AreEqual(HttpStatusCode.BadRequest, (await malo.ExecuteAsync(default)).StatusCode);
            Assert.AreEqual(1, propuestas.Content.Count);
            Assert.AreEqual(HttpStatusCode.NoContent, descartada.StatusCode);
            Assert.AreEqual(HttpStatusCode.NotFound, (await otraVez.ExecuteAsync(default)).StatusCode);
        }

        [TestMethod]
        public async Task Controller_AceptarPropuesta_Ok()
        {
            HorarioPreferibleController controller = Controller(NestoAPI.Models.Constantes.GruposSeguridad.ADMINISTRACION);
            _ = Propuesta(5, "HORARIO DE 10 A 14");

            var r = await controller.PostAceptarPropuesta(5, null) as OkNegotiatedContentResult<HorarioPreferibleDTO>;

            Assert.AreEqual("L-V 10:00-14:00", r.Content.Texto);
        }
    }
}
