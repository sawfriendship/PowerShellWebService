function logout() {
    $.ajax({
        url: '/logout',
        method: 'POST',
        cache: false,
        success: function(){
			console.log('ok')
        },
        error: function(){
            console.log('err')
        }
    })

}

function show_alert(text,status='success',timeout=3000,duration=500) {
    // 'success', 'info', 'warning', 'danger'
    var alert_html = $(`<div class="alert alert-${status}">${text}</div>`)
    $('#alerts').append(alert_html.css({display:'none'}));
    alert_html.show(100);
    setTimeout(function() {alert_html.hide(duration);}, timeout)
    setTimeout(function() {alert_html.remove();}, timeout+duration)
};


// $(document).ready(function() {console.log('app')});
