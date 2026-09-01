using System;
using System.Collections.Generic;

namespace NestoAPI.Models
{
    public class Constantes
    {
        public static class Aplicaciones
        {
            public const string NESTO_APP = "NestoApp";
            public const string NESTO_TIENDAS = "NestoTiendas";
        }

        public class Agencias
        {
            public const int AGENCIA_GLS = 1;
            public const int AGENCIA_GLOVO = 7;
            public const int AGENCIA_CORREOS_EXPRESS = 8;
            public const int AGENCIA_SENDING = 10;
            // NestoAPI#204: Canteras (Numero=11) cubre los envíos a Canarias. Operativa manual
            // por correo (no hay integración) y no admite contra reembolso. Mínimo del pedido
            // 400€ o línea de portes de 100€ (constantes IMPORTE_MINIMO_CANARIAS y Portes.CANARIAS).
            public const int AGENCIA_CANTERAS = 11;
            // Innovatrans (DataTrans DTX). Primera agencia con gestión remota server-side (insertar
            // envío + etiqueta ZPL vía SOAP). Alta en AgenciasTransporte con Numero=12.
            public const int AGENCIA_INNOVATRANS = 12;
            // CTT Express. Alta en AgenciasTransporte con Numero=13 y EsSombra=1 (agencia sombra:
            // compite en el comparador para medir cuántos envíos ganaría, pero nunca se auto-selecciona).
            public const int AGENCIA_CTT = 13;
            // Ciclo de vida del envío (EnviosAgencia.Estado). Numeración canónica unificada (#247),
            // común a todas las agencias y clientes: Pendiente (-1) → En curso (0, etiqueta creada) →
            // Tramitado (1, cerrado el día/entregado a la agencia) → Entregado (2) / Incidentado (3).
            // Entregado e Incidentado los rellena el poll de seguimiento por agencia (#248).
            public const int ESTADO_PENDIENTE = -1;
            public const int ESTADO_EN_CURSO = 0;
            public const short ESTADO_TRAMITADO = 1;
            public const short ESTADO_ENTREGADO = 2;
            public const short ESTADO_INCIDENTADO = 3;
            public const short ESTADO_DEVUELTO = 4; // devuelto a origen (fallo terminal de entrega)
            public const decimal REEMBOLSO_NO_COBRAR = -1M;
        }

        public static class Almacenes
        {
            public const string ALCOBENDAS = "ALC";
            public const string ALGETE = "ALG";
            public const string REINA = "REI";
        }

        /// <summary>
        /// Series de facturación.
        /// Carlos 09/12/25: Issue #245
        /// </summary>
        public static class Series
        {
            // Verifactu #39 (20/08/26): VC y DV eliminadas del código (VC nunca existió en la
            // BD; DV deja de usarse — sus abonos van por RV). Los datos históricos DV siguen
            // en la BD y se imprimen con la plantilla por defecto (fallback de LeerSerie).
            public const string SERIE_POR_DEFECTO = "NV";
            public const string SERIE_CURSOS = "CV";
            public const string SERIE_UNION_LASER = "UL";
        }

        public static class Clientes
        {
            public const char SEPARADOR_TELEFONOS = '/';
            public const short ESTADO_DISTRIBUIDORES = 6;
            /// <summary>Nesto#436: ruta de los clientes con dirección extranjera ("00", Fuera de
            /// Madrid): sus CPs no están en nuestra tabla y no llevan ruta de reparto propia.</summary>
            public const string RUTA_CLIENTES_EXTRANJEROS = "00";
            /// <summary>
            /// NestoAPI#424: el catálogo COMPLETO de estados de la ficha de cliente, que hasta
            /// ahora era conocimiento tácito. Regla general: cualquier valor NEGATIVO es una ficha
            /// ANULADA (por eso las consultas de fichas vivas filtran Estado &gt;= 0). Este catálogo
            /// lo comparten TiendasNuevaVision (EstadoClienteNoProfesional) y el módulo de login de
            /// PrestaShop: si se añade un estado, avisar a los tres.
            /// </summary>
            public static class Estados
            {
                /// <summary>Ficha anulada (cualquier negativo lo es; -1 es el habitual).</summary>
                public const short NULO = -1;
                /// <summary>Cliente normal, con visita presencial del vendedor.</summary>
                public const short VISITA_PRESENCIAL = 0;
                /// <summary>Cliente atendido por el equipo telefónico (mismo número que el estado
                /// 9 de VENDEDOR telefónico, pero son catálogos DISTINTOS: no confundirlos).</summary>
                public const short VISITA_TELEFONICA = 9;
                /// <summary>Alta reciente, pendiente de primera visita; al completar el NIF pasa a
                /// su estado definitivo.</summary>
                public const short PRIMERA_VISITA = 5;
                public const short DISTRIBUIDOR = 6;
                /// <summary>Comisiona al vendedor aunque no se le visite.</summary>
                public const short COMISIONA_SIN_VISITA = 7;
                /// <summary>NestoAPI#424: cliente NO profesional, compra a precio de público final.
                /// Los clientes extranjeros se dan de alta así (Nesto: CrearClienteViewModel).
                /// TiendasNuevaVision lo usa para EsProfesional y el picking y los pedidos de
                /// canales externos para EsPrecioPublicoFinal.</summary>
                public const short PUBLICO_FINAL = 8;
                public const short SIN_ACCION_COMERCIAL_SOLO_ESTETICA = 93;
                public const short SIN_ACCION_COMERCIAL_SOLO_PELUQUERIA = 11;
                public const short SIN_ACCION_COMERCIAL_ESTETICA_Y_PELUQUERIA = 22;
            }

            public static class EstadosMandatos
            {
                public const int EN_PODER_DEL_CLIENTE = 2;
            }
            public const short CARGO_POR_DEFECTO = 14;
            public const string DIAS_EN_SERVIR_POR_DEFECTO = "11111";
            public const string GRUPO_POR_DEFECTO = "0";
            public const string SECUENCIA_POR_DEFECTO = "FRST";

            public static class PersonasContacto
            {
                public const short ESTADO_POR_DEFECTO = 0;
                public const short CARGO_FACTURA_POR_CORREO = 22;
                public const short CARGO_FACTURAS_TRIMESTRE_POR_CORREO = 28;
                public const short CARGO_COBROS = 1;
            }

            public static class TiposExtracto
            {
                public const string TIPO_FACTURA = "1";
                public const string TIPO_CARTERA = "2";
            }
        }

        public static class Comisiones
        {
            // NestoAPI#185: valor centinela para FaltaParaSalto y FinalTramo cuando el
            // vendedor está en el último tramo (sin límite superior). No se puede usar
            // decimal.MaxValue porque Newtonsoft.Json lo parsea vía double y pierde
            // precisión, haciendo que el cliente no pueda convertirlo de vuelta a
            // decimal (OverflowException). -1 es inequívoco: un importe/diferencia
            // negativo no tiene sentido semántico en estos campos.
            public const decimal SIN_LIMITE_TRAMO = -1m;
        }

        public static class Correos
        {
            public const string COMPRAS = "compras@nuevavision.es";
            public const string CORREO_ADMON = "administracion@nuevavision.es";
            public const string CORREO_DIRECCION = "direccion@nuevavision.es";
            public const string TIENDA_ALCOBENDAS = "alcobendas@nuevavision.es";
            public const string INFORMATICA = "carlosadrian@nuevavision.es";
            public const string LOGISTICA = "logistica@nuevavision.es";
            /// <summary>NestoAPI#361: avisos del picking automatico de las 11h.</summary>
            public const string ALMACEN = "almacen@nuevavision.es";
            public const string TIENDA_ONLINE = "tiendaonline@nuevavision.es";
            public const string TIENDA_REINA = "tienda@nuevavision.es";
        }

        public static class Cuentas
        {
            public const string COMISIONES_BANCO_COBRO_TPV = "62600008";
            // Issue #159: cuenta específica para comisión contra reembolso. Antes era 75900000
            // (Ingresos por servicios diversos), ahora 62400000 para tratarla como minoración
            // de gasto de transporte, en coherencia con las cuentas 624xxx de las agencias.
            public const string CUENTA_PORTES_VENTA_GENERAL = "62400000";
            public const string CUENTA_PORTES_GLOVO = "62400017";
            public const string CUENTA_PORTES_CEX = "62400005";
            public const string CUENTA_PORTES_ONTIME = "62400002";
            public const short ESTADO_ACTIVA = 0;
            public const short NIVEL_MAXIMO = 8;
        }

        public static class DiariosContables
        {
            public const string COMISIONES_BANCO = "_ComisBanc";
        }

        public static class DiariosProducto
        {
            public const string MONTAR_KIT = "_MontarKit";
            public const string ENTREGA_FACTURADA = "_EntregFac";
        }

        public static class Dominios
        {
            public const string PRINCIPAL = "NUEVAVISION";
        }

        public static class Empresas
        {
            public const string DELEGACION_POR_DEFECTO = "ALG";
            public const string EMPRESA_ESPEJO_POR_DEFECTO = "3";
            public const string EMPRESA_POR_DEFECTO = "1";
            public const string FORMA_VENTA_POR_DEFECTO = "VAR";
            public const string IVA_POR_DEFECTO = "G21";
            public const string IVA_REDUCIDO = "R10";
        }


        public static class EstadosLineaVenta
        {
            public const int PRESUPUESTO = -3;
            public const int NOTA_ENTREGA = -2;
            public const int PENDIENTE = -1;
            public const int EN_CURSO = 1;
            public const int ALBARAN = 2;
            public const int FACTURA = 4;
        }

        public static class ExtractosCliente
        {
            public static class Estados
            {
                // El apunte vivo y corriente. Ojo: en la BD conviven "NRM" y NULL para lo mismo,
                // así que los filtros de apuntes normales tienen que aceptar los dos.
                public const string NORMAL = "NRM";
                public const string DEUDA_VENCIDA = "DVD";
                public const string RETENIDO = "RTN";
            }
            public static class TiposApunte
            {
                public const string FACTURA = "1";
                // NestoAPI#332: la CARTERA es el tipo que entra en las remesas de cobro
                // (criterio de Carlos 20/07/26: el 2, NO el 1).
                public const string CARTERA = "2";
                public const string PAGO = "3";
                public const string IMPAGADO = "4";
            }
        }

        public static class Facturas
        {
            public static class TiposDocumento
            {
                public const string FACTURA = "FACTURA";
                public const string FACTURA_RECTIFICATIVA = "FACTURA RECTIFICATIVA";
                public const string FACTURA_PROFORMA = "FACTURA PROFORMA";
                public const string PEDIDO = "PEDIDO";
                public const string NOTA_ENTREGA = "NOTA DE ENTREGA";
                public const string ALBARAN = "ALBARÁN";
            }
        }

        public static class FormasPago
        {
            public const string EFECTIVO = "EFC";
            public const string RECIBO_BANCARIO = "RCB";
            public const string TRANSFERENCIA = "TRN";
            public const string TARJETA = "TAR";

            // Formas de pago seguras para clientes con deuda (cobro garantizado antes de entregar)
            public static readonly string[] FORMAS_PAGO_SEGURAS = { EFECTIVO, TRANSFERENCIA, TARJETA };
        }

        public static class FormasVenta
        {
            public const string AMAZON = "STK";
            public const string TIENDA_ONLINE = "WEB";
            public const string PERFUMES_CLUB = "QRU";
            public const string MIRAVIA = "BLT";

            /// <summary>
            /// NestoAPI#435: pedidos que crea el propio cliente desde la app movil
            /// (TiendasNuevaVision). La forma de venta "Aplicacion Moviles" ya estaba dada de alta
            /// en la tabla FormasVenta de la empresa 1, con VisiblePorComerciales = false.
            /// OJO: NO es un canal externo y no debe entrar en CANALES_EXTERNOS. Los canales
            /// externos traen sus portes ya calculados por la plataforma de origen y por eso se
            /// respetan; detras de APP no hay ninguna plataforma que los calcule, y no queremos
            /// que sea el cliente quien diga cuanto cuesta el envio (igual que no dice el precio):
            /// los portes los calcula el servidor con GestorPortes, como en cualquier otro pedido.
            /// </summary>
            public const string APP = "APP";

            /// <summary>
            /// Los mismos cuatro codigos, pero en array y publicos, para poder usarlos DENTRO de
            /// una consulta de Entity Framework: EsCanalExterno es un metodo de C# y EF6 no sabe
            /// traducirlo a SQL, mientras que un Contains sobre esta coleccion se convierte en un
            /// IN (...) — y ahi la comparacion la hace SQL Server, que ignora el relleno de los
            /// char. Fuente unica: el HashSet de abajo se construye a partir de este array.
            /// </summary>
            public static readonly string[] CANALES_EXTERNOS = { AMAZON, TIENDA_ONLINE, PERFUMES_CLUB, MIRAVIA };

            private static readonly HashSet<string> _canalesExternos = new HashSet<string>(CANALES_EXTERNOS);

            public static bool EsCanalExterno(string formaVenta)
            {
                return !string.IsNullOrWhiteSpace(formaVenta) && _canalesExternos.Contains(formaVenta.Trim());
            }

            /// <summary>
            /// NestoAPI#435: formas de venta que el almacen prepara y envia como un pedido de
            /// tienda online. La app (APP) SI entra: un pedido suyo se prepara y se envia igual
            /// que uno de la web, aunque no sea un canal externo a efectos de portes ni de
            /// validaciones. Array (no HashSet) para poder usarlo DENTRO de una consulta de
            /// Entity Framework, igual que CANALES_EXTERNOS.
            /// </summary>
            public static readonly string[] PREPARACION_TIENDA_ONLINE = { AMAZON, TIENDA_ONLINE, PERFUMES_CLUB, MIRAVIA, APP };

            /// <summary>
            /// NestoAPI#435: formas de venta cuyo albaran se imprime a precio de publico final
            /// (cuando ademas el cliente esta en estado 8 y el vendedor es NV). APP se queda
            /// FUERA a proposito: los clientes de la app son mayoritariamente profesionales y su
            /// albaran debe salir a su precio, no al de publico final.
            /// </summary>
            public static readonly string[] PRECIO_PUBLICO_FINAL = { AMAZON, TIENDA_ONLINE, PERFUMES_CLUB };

            private static readonly HashSet<string> _preparacionTiendaOnline = new HashSet<string>(PREPARACION_TIENDA_ONLINE);

            /// <summary>
            /// NestoAPI#435: ¿el almacen prepara este pedido como uno de tienda online?
            /// </summary>
            public static bool EsPreparacionTiendaOnline(string formaVenta)
            {
                return !string.IsNullOrWhiteSpace(formaVenta) && _preparacionTiendaOnline.Contains(formaVenta.Trim());
            }
        }

        public class GruposSeguridad
        {
            public const string ADMINISTRACION = "Administración";
            public const string ALMACEN = "Almacén";
            public const string COMPRAS = "Compras";
            public const string DIRECCION = "Dirección";
            public const string FACTURACION = "Facturación";
            public const string TIENDA_ON_LINE = "TiendaOnline";
            public const string TIENDAS = "Tiendas";
        }

        public static class TiposPagoTPV
        {
            /// <summary>Enlace de pago de siempre: contabiliza un cobro contra el extracto del cliente.</summary>
            public const string TPV_VIRTUAL = "TPVVirtual";

            /// <summary>
            /// NestoAPI#436: cobro con tarjeta de un pedido que ha creado el propio cliente desde la
            /// app. NO contabiliza como el enlace de pago: cuando Redsys confirma, el cobro entra
            /// como Prepago del pedido (igual que hace CanalesExternos con PrestaShop), y se aplica
            /// al facturarlo. El numero de pedido viaja en la columna Documento del PagoTPV.
            /// </summary>
            public const string PEDIDO_APP = "PedidoApp";
        }

        public static class Prepagos
        {
            /// <summary>
            /// Cuenta de Redsys, donde esta el dinero de los cobros con tarjeta hasta que se
            /// factura el pedido. Es la que usa PrestaShop para sus prepagos.
            /// </summary>
            public const string CUENTA_REDSYS = "57200013";
        }

        public static class EstadosPagoTPV
        {
            public const string PENDIENTE = "Pendiente";
            public const string AUTORIZADO = "Autorizado";
            public const string DENEGADO = "Denegado";
        }

        public static class NivelRiesgoPagos
        {
            public const short NO_TIENE_DEUDA = 1;
            public const short TIENE_DEUDA_NO_VENCIDA = 2;
            public const short TIENE_DEUDA_VENCIDA = 3;
            public const short TIENE_IMPAGADOS_PENDIENTES = 4;
            public const short CONTADO_RIGUROSO = 5;
        }

        public static class Pedidos
        {
            public const string PERIODO_FACTURACION_FIN_DE_MES = "FDM";
            public const string PERIODO_FACTURACION_NORMAL = "NRM";
            public const string RUTA_GLOVO = "GLV";

            // Rutas para facturación masiva
            public const string RUTA_PROPIA_16 = "16";
            public const string RUTA_PROPIA_AT = "AT";
            public const string RUTA_AGENCIA_FW = "FW";
            public const string RUTA_AGENCIA_00 = "00";

            // Issue #159: a partir de esta fecha el flag NoCobrarComisionReembolso se ignora
            // y siempre se aplica la comisión cuando procede.
            public static readonly DateTime FECHA_CORTE_NO_COBRAR_COMISION_REEMBOLSO = new DateTime(2026, 9, 1);
        }
        public static class ParametrosUsuario
        {
            // #256: almacenes cuyo stock se muestra en la plantilla de venta (CSV, p. ej.
            // "ALG,ALC,REI" o "ALG"). El contrato de la clave es común a Nesto y NestoApp.
            public const string ALMACENES_PLANTILLA_VENTA = "AlmacenesPlantillaVenta";

            /// <summary>Usuario bajo el que viven los parametros que no son de nadie en concreto.</summary>
            public const string USUARIO_POR_DEFECTO = "(defecto)";

            /// <summary>
            /// NestoAPI#361: destinatarios del aviso del picking automatico de las 11h, separados
            /// por ; o por coma. Si esta vacio se usa Correos.ALMACEN. Se puede cambiar sin
            /// desplegar.
            /// </summary>
            public const string CORREO_AVISO_PICKING_AUTOMATICO = "CorreoAvisoPickingAutomatico";
        }
        public static class PlazosPago
        {
            public const string CONTADO = "CONTADO";
            public const string CONTADO_RIGUROSO = "CR";
            public const string PREPAGO = "PRE";

            /// <summary>
            /// NestoAPI#396 (Carlos 21/08/26): importe mínimo que debe quedar en CADA efecto.
            /// Vive aquí y no en cada sitio porque la misma regla de negocio se aplicaba en dos
            /// lados con números distintos: el selector de plazos (PlazosPagoController) usaba
            /// 100 € y el aviso del correo de nuevo pedido 150 €, así que el correo marcaba como
            /// sospechosos plazos que el propio selector le había ofrecido al vendedor. El valor
            /// bueno es 150; el 100 se quedó sin actualizar.
            /// </summary>
            public const decimal IMPORTE_MINIMO_EFECTO = 150M;

            /// <summary>
            /// NestoAPI#396: financiación media (días ponderados, columna Financiacion de
            /// PlazosPago) hasta la que NO se exige el mínimo por efecto. 30 días se los damos a
            /// todo el mundo, así que por debajo de eso no hay nada que revisar.
            /// </summary>
            public const decimal FINANCIACION_ESTANDAR_DIAS = 30M;
        }

        public static class Portes
        {
            public const decimal PROVINCIAL = 3.5M;
            public const decimal PENINSULAR = 7M;
            public const decimal BALEARES = 20M;
            public const decimal CANARIAS = 100M;
            public const decimal INCREMENTO_REEMBOLSO = 3M;

            // NestoAPI#174: texto de la línea de comisión por contra reembolso. Es el
            // identificador real de la línea: la cuenta contable (CUENTA_PORTES_VENTA_GENERAL)
            // coincide con la de portes, así que el texto es lo que distingue una de otra
            // al detectar líneas existentes (búsqueda case-insensitive de "reembolso").
            public const string TEXTO_COMISION_REEMBOLSO = "Comisión contra reembolso";

            // NestoAPI#187: aviso que se devuelve al cliente en ValidarServirJunto cuando
            // el pedido aplica comisión contra reembolso y se está desmarcando servirJunto.
            // Informa de que, tras NestoAPI#174, cada envío llevará su propia comisión.
            public const string AVISO_COMISION_REEMBOLSO_SPLIT =
                "Si desmarcas Servir Junto, se aplicará una comisión de contra reembolso por cada envío que se haga al cliente. ¿Quieres continuar?";
        }

        public static class Productos
        {
            public const string ALMACEN_POR_DEFECTO = "ALG";
            public const string ALMACEN_TIENDA = "REI";
            public const short ESTADO_A_EXTINGUIR = 4;
            public const short ESTADO_NO_SOBRE_PEDIDO = 0;
            public const string FAMILIA_BONIFICACION = "Bonificac";
            public const string GRUPO_ACCESORIOS = "ACC";
            public const string GRUPO_APARATOS = "APA";
            public const string GRUPO_COSMETICA = "COS";
            public const string GRUPO_CURSOS = "CUR";
            public const string GRUPO_MATERIAS_PRIMAS = "MTP";
            public const string GRUPO_PELUQUERIA = "PEL";
            public const string SUBGRUPO_MUESTRAS = "MMP";
            public const int DEPOSITO_DIAS_ESTADISTICA = 60;
            public const decimal PORCENTAJE_MAXIMO_REGALOS = 0.10m; // 10%

            /// <summary>
            /// Sentinel de <c>PrestashopProductos.PVP_IVA_Incluido</c>: este producto se vende al
            /// público al MISMO precio que al profesional (público = PVP + IVA, sin el descuento
            /// del 30 %). Los tres valores posibles del campo son:
            ///
            ///   · positivo → ese es el precio público con IVA (el profesional y el público difieren)
            ///   · NULL     → el caso mayoritario: el público lleva el descuento por defecto del 30 %,
            ///                y lo calcula el módulo de PrestaShop (la regla del 30 % vive SOLO allí)
            ///   · -1       → público = profesional
            ///
            /// Es negativo a propósito: un precio negativo es imposible por naturaleza, así que
            /// cualquier consumidor que valide "es un precio" (&gt; 0) lo descarta solo, sin
            /// necesidad de conocer esta convención. Un sentinel positivo grande (int.MaxValue)
            /// pasaría esas validaciones y viajaría como un precio de verdad.
            ///
            /// Misma convención que <see cref="Agencias.REEMBOLSO_NO_COBRAR"/>.
            /// </summary>
            public const decimal PVP_IVA_MISMO_QUE_PROFESIONAL = -1M;

            /// <summary>
            /// El precio profesional (PVP) es el público MENOS el 30 %, así que el público sale de
            /// dividir por esto. Ojo: no es lo mismo que multiplicar por 1,30 — un descuento del
            /// 30 % sobre el público equivale a un margen del 42,86 % sobre el profesional.
            ///
            /// ⚠️ Este mismo factor está configurado en el módulo NestoSync de PrestaShop, que es
            /// el dueño del cálculo. Aquí solo se usa cuando PrestaShop NO da precio (producto que
            /// no está en la tienda, o tienda que no responde). Si allí se cambia el descuento por
            /// defecto, hay que cambiarlo también aquí o los dos precios divergirán.
            /// </summary>
            public const decimal FACTOR_PRECIO_PROFESIONAL = 0.7M;

            /// <summary>
            /// Grupos de producto que generan Ganavisiones (puntos para bonificaciones).
            /// Issue #94: Sistema Ganavisiones
            /// </summary>
            public static readonly string[] GRUPOS_BONIFICABLES_CON_GANAVISIONES = { GRUPO_COSMETICA, GRUPO_ACCESORIOS, GRUPO_PELUQUERIA };

            /// <summary>
            /// Valor en EUR de cada Ganavisión para bonificaciones.
            /// Issue #94: Sistema Ganavisiones
            /// </summary>
            public const decimal VALOR_GANAVISION_EN_EUROS = 10m;
        }

        public static class Proveedores
        {
            public static class PersonasContacto
            {
                public const int INFORMACION_PRODUCTO_DEPOSITO = 27;
                public const int RECEPCION_PEDIDOS = 3;
            }
        }
        public class Sedes
        {
            public static List<string> ListaSedes = new List<string>
            {
                "ALG",
                "REI",
                "ALC"
            };
        }
        public static class TiposExtractoCliente
        {
            public const string SIN_ESPECIFICAR = "0";
            public const string FACTURA = "1";
            public const string CARTERA = "2";
            public const string PAGO = "3";
            public const string IMPAGADO = "4";
        }

        public static class TiposLineaCompra
        {
            public const string TEXTO = "0";
            public const string PRODUCTO = "1";
            public const string CUENTA_CONTABLE = "2";
            public const string INMOVILIZADO = "3";
        }

        public static class TiposLineaVenta
        {
            public const int TEXTO = 0;
            public const int PRODUCTO = 1;
            public const int CUENTA_CONTABLE = 2;
            public const int INMOVILIZADO = 3;
        }

        public static class Ubicaciones
        {
            //public const int ESTADO_A_INSERTAR = -100;
            public const int ESTADO_A_MODIFICAR_CANTIDAD = -101;
            public const int ESTADO_REGISTRO_MONTAR_KITS = -102;
            public const int UBICADO = 0;
            public const int PENDIENTE_UBICAR = 2;
            public const int RESERVADO_PICKING = 3;
            public const int RESERVADO_REPOSICION = 3;
            public const int ENTREGADO_NOTA_ENTREGA = -3;
        }

        public static class Picking
        {
            public const int HORA_MAXIMA_AMPLIAR_PEDIDOS = 11;

            /// <summary>
            /// NestoAPI#361: "no habia nada que sacar" es un resultado NORMAL del picking, no un
            /// fallo. Se identifica por este codigo para poder tratarlo distinto del resto
            /// (correo tranquilizador en vez de alarma en el picking automatico).
            /// </summary>
            public const string ERROR_SIN_STOCK = "PICKING_SIN_STOCK";
        }

        public static class ClientesEspeciales
        {
            public const string EL_EDEN = "15191";
            public const string TIENDA_ONLINE = "31517";
            public const string AMAZON = "32624";
            public const string PUBLICO_FINAL = "10458";
            /// <summary>Ficticio interno de agrupación de materiales de cursos (NIF '6').
            /// Decisión Carlos 17/08/26: deja de usarse para facturar (los materiales salen
            /// por diario); mientras existan facturas suyas, se declaran como simplificadas.</summary>
            public const string MATERIALES_CURSOS = "31794";

            // Clientes ficticios de venta a consumidor final: sus facturas son SIMPLIFICADAS
            // (F2 sin destinatario, art. 6.1.d RD 1619/2012). Criterio único compartido por
            // Verifactu (#325) y por la subida de facturas a Amazon (#366), que las excluye.
            private static readonly HashSet<string> _clientesFacturaSimplificada =
                new HashSet<string> { AMAZON, TIENDA_ONLINE, PUBLICO_FINAL, MATERIALES_CURSOS };

            public static bool EsClienteFacturaSimplificada(string cliente)
                => !string.IsNullOrWhiteSpace(cliente) && _clientesFacturaSimplificada.Contains(cliente.Trim());

            // NestoAPI#391: el listado de NIF incorrectos los excluye por SQL, así que necesita
            // la lista completa (el resto del código usa el predicado de arriba).
            public static System.Collections.Generic.IReadOnlyCollection<string> ClientesFacturaSimplificada
                => _clientesFacturaSimplificada;
        }

        public static class Vendedores
        {
            public const int ESTADO_VENDEDOR_PRESENCIAL = 0;
            public const int ESTADO_VENDEDOR_MINI = 2;
            public const int ESTADO_VENDEDOR_TELEFONICO = 9;
            public const int ESTADO_VENDEDOR_PELUQUERIA = 4;
            public const int ESTADO_VENDEDOR_PARA_ANULAR = 99;
            public const string VENDEDOR_GENERAL = "NV";
        }

        public static class SeguimientosCliente
        {
            public static class Tipos
            {
                public const string TIPO_VISITA_PRESENCIAL = "V";
                public const string TIPO_VISITA_TELEFONICA = "T";
            }
        }

        public static class Contabilidad
        {
            public static class Diarios
            {
                public const string DIARIO_CIERRE = "_ASIENTCIE";
                // Nesto#340 (A4.1): diario donde caen los cobros de reembolso de las agencias.
                public const string DIARIO_REEMBOLSOS = "_Reembolso";
            }

            public static class TiposCuenta
            {
                public const string CUENTA_CONTABLE = "1";
                public const string CLIENTE = "2";
                public const string PROVEEDOR = "3";
            }
        }

        public static class ExtractoRuta
        {
            public const string TIPO_RUTA_PEDIDO = "P";
        }

        public static class Redsys
        {
            public const string MERCHANT_CODE = "329515704";
            public const string TERMINAL_P2F = "2";
            public const string TERMINAL_TPV_VIRTUAL = "1";
        }
    }
}