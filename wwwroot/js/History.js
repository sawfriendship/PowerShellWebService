// Please see documentation at https://docs.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

var min_id = 0;
var max_id = 0;

function has_key(obj,key){
    for (k in obj) {if(k==key){return true}}
    return false
}

var table = $('#LogTable table')
var thead = $('<thead><tr><th>id</th><th>BeginDate</th><th>UserName</th><th>IPAddress</th><th>Wrapper</th><th>Script</th><th>Method</th><th>Success</th><th>HadErrors</th><th>Error</th></tr></thead>')
var tbody = $('<tbody></tbody>')
table.append(thead);
table.append(tbody)

function load_modal(event_id) {
    $('#Modals').empty()
    var _new_modal = $('#ModalTemplate').clone().addClass('_new_modal').appendTo('#Modals')
    _new_modal.find('.modal-title').html(`Event ${event_id}`)

    $.ajax({
        url: '/log',
        method: 'GET',
        data: {id:event_id},
        success: function(responce) {
            var jString = JSON.stringify(responce, null, 2);
            if (responce.Success && responce.Count > 0) {
                window.__renderJSONToHTMLTable({data: responce.Data, rootElement: '._new_modal .modal-body'});
            }

            $('._new_modal .modal-body').append($(
                `
                <div id="Result" class="">
                    <h3>Raw data</h3>
                    <textarea id="_result" class="form-control bg-dark text-light border border-4 rounded rounded-5" rows="20" placeholder="Result" readonly>${jString}</textarea>
                    <span class="btn btn-dark _btn_copy"><i class="bi bi-clipboard"></i></span>
                </div>
                `
            ));
            $('._btn_copy').on('click', ()=>navigator.clipboard.writeText($('#_result').val()));
            // console.log('ok')

        },
        error: function(responce) {
            // console.log('err')
        },
        complete: function(){
            $('._new_modal .modal-body').append($(`<p>URL: <a href="${this.url}" target="_blank">${this.url}</a></p>`))
        },
    });

    $('._new_modal').modal('show')
}

function load_param() {
    var form_items = [
        '_interval','_count',
        '_wrapper','_wrapper_op','_script','_script_op',
        '_begindate1_op','_begindate1','_begindate2_op','_begindate2',
        '_username_op','_username',
        '_ipaddress','_ipaddress_op',
        '_method','_method_op'
    ]
    
    form_items.forEach(function(item){
        $(`form [name=${item}]`).on('click change keyup', function(e){
            if ($(this).val()) {
                localStorage[item]=$(this).val()
            } else {
                localStorage.removeItem(item)
            }
            if (e.keyCode === 13) {
                // window.location.reload();
                min_id = 0;
                max_id = 0;
                tbody.html('')
                load_data(direction=1);
            }
        });
        if (has_key(localStorage,item)) {
            $(`form [name=${item}]`).val(localStorage[item])
        } else {
            localStorage[item] = $(`form [name=${item}]`).val()
        }
    })
}

function load_data(direction=0) {
    var filter_ = {'$limit':10,'$order':1}
    var filter = {}
    if (direction == 0 || max_id == 0 || min_id == 0) {
        filter = Object.assign({},filter_,{'$asc':0,'$limit':10})
    } else if (direction > 0 && max_id != 0 && min_id != 0) {
        filter = Object.assign({},filter_,{'$asc':1,'id!<':max_id})
    } else if (direction < 0 && max_id != 0 && min_id != 0) {
        filter = Object.assign({},filter_,{'$asc':0,'id!>':min_id})
    } else {}

    if (has_key(localStorage,'_count') && localStorage['_count']) {filter['$limit'] = localStorage['_count']}
    if (has_key(localStorage,'_wrapper') && localStorage['_wrapper']) {filter[`Wrapper${localStorage['_wrapper_op']}`] = localStorage['_wrapper']}
    if (has_key(localStorage,'_script') && localStorage['_script']) {filter[`Script${localStorage['_script_op']}`] = localStorage['_script']}
    if (has_key(localStorage,'_begindate1') && localStorage['_begindate1']) {filter[`BeginDate${localStorage['_begindate1_op']}`] = localStorage['_begindate1']}
    if (has_key(localStorage,'_begindate2') && localStorage['_begindate2']) {filter[`BeginDate${localStorage['_begindate2_op']}`] = localStorage['_begindate2']}
    if (has_key(localStorage,'_username') && localStorage['_username']) {filter[`UserName${localStorage['_username_op']}`] = localStorage['_username']}
    if (has_key(localStorage,'_ipaddress') && localStorage['_ipaddress']) {filter[`IPAddress${localStorage['_ipaddress_op']}`] = localStorage['_ipaddress']}
    if (has_key(localStorage,'_method') && localStorage['_method']) {filter[`Method${localStorage['_method_op']}`] = localStorage['_method']}

    console.log(filter)

    $.ajax({
        url: '/log',
        method: 'GET',
        data: filter,
        success: function(responce) {
            var data = responce.Data;
            if (responce.Success && responce.Count > 0) {
                var row = '';
                data.forEach(function(item){
                    var sclass = 'table-light';
                    if (item.HadErrors) {sclass = 'table-warning'}
                    if (!item.Success) {sclass = 'table-danger'}
                    var row = $(`<tr row_id="${item.id}" class="${sclass}" style="display:none"><td>${item.id}</td><td>${item.BeginDate}</td><td>${item.UserName}</td><td>${item.IPAddress}</td><td>${item.Wrapper}</td><td>${item.Script}</td><td>${item.Method}</td><td>${item.Success}</td><td>${item.HadErrors}</td><td>${item.Error}</td></tr>`)
                    if (max_id == 0 || min_id == 0) {
                        min_id = item.id
                        max_id = item.id
                    }
                    if (item.id > max_id) {
                        // console.log('>')
                        max_id = item.id
                        tbody.prepend(row)
                    } else if (item.id < min_id) {
                        // console.log('<')
                        min_id = item.id
                        tbody.append(row)
                    } else {}
                    row.show(320)
                    
                })
            }
            // console.log('ok')
        },
        error: function(responce) {
            // console.log('err')
        },
        // complete: function(){
        //     $('#ParamURL').html($(`<p>URL: <a href="${this.url}" target="_blank">${this.url}</a></p>`))
        // },
    });

}


// https://doka.guide/js/infinite-scroll/?ysclid=li4zwmwam8358176772

function checkPosition() {
    // Нам потребуется знать высоту документа и высоту экрана:
    const height = document.body.offsetHeight
    const screenHeight = window.innerHeight
    // Они могут отличаться: если на странице много контента,
    // высота документа будет больше высоты экрана (отсюда и скролл).

    // Записываем, сколько пикселей пользователь уже проскроллил:
    const scrolled = window.scrollY

    // Обозначим порог, по приближении к которому
    // будем вызывать какое-то действие.
    // В нашем случае — четверть экрана до конца страницы:
    const threshold = height - screenHeight / 4

    // Отслеживаем, где находится низ экрана относительно страницы:
    const position = scrolled + screenHeight

    if (position >= threshold) {
        // Если мы пересекли полосу-порог, вызываем нужное действие.
        load_data(-1)
    }
}

function throttle(callee, timeout) {
    let timer = null
    return function perform(...args) {
        if (timer) return
        timer = setTimeout(() => {
            callee(...args)
            clearTimeout(timer)
            timer = null
        }, timeout)
    }
}

// https://nikhil-vartak.github.io/json-to-html-converter/

var reload_timer_id = 0;

$(document).ready(function() {
    if (has_key(localStorage,'_interval')) {
        $(`form [name=_interval]`).val(localStorage['_interval'])
    } else {
        $(`form [name=_interval]`).val(10)
        localStorage['_interval'] = $(`form [name=_interval]`).val()
    }
    reload_timer_id = setInterval(() => load_data(1), localStorage['_interval']*1000);
    $('form [name=_interval]').on('click change keyup', function(e){
        clearInterval(reload_timer_id);
        reload_timer_id = setInterval(() => load_data(1), localStorage['_interval']*1000);
    });

    min_id = 0;
    max_id = 0;

    load_data(0);
    load_param();
    
    $('#LogTable tbody').on('click', 'tr', function(e){var row_id = $(this).attr('row_id');load_modal(row_id);});
    $('#Modals').on('click', '._modal_close', function(e){$('._new_modal').modal('hide');});
    $('._btn_load_more').on('click', function(e){load_data(-1);});
    window.addEventListener('scroll', throttle(checkPosition, 250))
});
