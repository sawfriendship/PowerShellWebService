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

$(document).ready(function() {
    console.log('app')
});
